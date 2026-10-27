using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NTRModern;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace NDSRenderer;

/// <summary>A picture that the renderer drew, as RGBA bytes from the top row down.</summary>
public sealed record Screenshot(int Width, int Height, byte[] Pixels);

/// <summary>
/// <c>FrameRenderer</c> draws DS frames with Vulkan, with the top screen above the bottom one unless you only show one
/// of them. Please note that <see cref="Submit"/> has to be called from <see cref="Video.FrameFinished"/>, and
/// everything else from one thread of your choice.
/// </summary>
public sealed unsafe class FrameRenderer : IDisposable
{
    public const int Width = 256;
    public const int Height = 2 * Video.VisibleLines;

    private const int Engines = 2;
    private const int ObjectCount = 128;
    private const int ObjectGroups = ObjectCount / 32;
    private const int OAMKilobyte = VideoFrame.OAMKilobyte;

    private const int LinesSize = Video.VisibleLines * VideoFrame.WordsPerLine * sizeof(uint);
    private const int LayoutOfLineOffset = LinesSize;
    private const int ObjectLinesOffset = LayoutOfLineOffset + (Video.VisibleLines * sizeof(uint));
    private const int SuppliedColumnsOffset = ObjectLinesOffset + (Video.VisibleLines * Engines * ObjectGroups * sizeof(uint));
    private const int MarginTilesOffset = SuppliedColumnsOffset + (FrameMargins.Engines * FrameMargins.Backgrounds * sizeof(uint));
    private const int MarginWindowsOffset = MarginTilesOffset + (FrameMargins.Engines * FrameMargins.Backgrounds * 2 * FrameMargins.ColumnsPerSide * FrameMargins.Rows * sizeof(ushort));
    private const int InsetsOffset = MarginWindowsOffset + (FrameMargins.Engines * FrameMargins.Windows * Video.VisibleLines * sizeof(uint));
    private const int RenderingOffset = InsetsOffset + (FrameInsets.Words * sizeof(uint));
    private const int RenderingHeaderWords = 5;
    private const int ControlRegistersOffset = RenderingOffset + (RenderingHeaderWords * sizeof(uint));
    private const int CopiesStartOffset = ControlRegistersOffset + RenderingEngine.ControlRegistersSize;
    private const int LayoutsOffset = CopiesStartOffset + sizeof(uint);
    private const int LayoutSize = VideoFrame.VideoMemoryKilobytes * sizeof(ushort);
    private const int SmallestFrameSize = LayoutsOffset + LayoutSize + (VideoFrame.VideoMemoryKilobytes * VideoFrame.KilobyteSize);

    private const int PolygonWords = 55;
    private const int PolygonHeaderWords = 5;
    private const int VertexWords = 5;
    private const uint FacingFlag = 1 << 4;
    private const uint DotFlag = 1 << 5;
    private const uint ShadowMaskFlag = 1 << 6;
    private const uint ShadowFlag = 1 << 7;
    private const uint ShadowMode = 3 << 4;
    private const uint PolygonID = 0x3F << 24;
    private const int FinalColorShift = 4;
    private const uint FinalColorFraction = 0xF;
    private const int RenderingLayers = 7;
    private const uint ScreenWindow = 0;
    private const uint LeftWindow = 1;
    private const uint RightWindow = 2;
    private const int RenderingWindows = 3;
    private const int RenderingSize = RenderingWindows * RenderingLayers * Width * Video.VisibleLines * sizeof(uint);

    private const uint DisplaySwap = 1 << 15;

    private const uint ThreadGroupSize = 8;
    private const uint FinishGroupSize = 8;
    private const uint MaxScale = 16;

    private static readonly byte[] ObjectHeights = [8, 16, 32, 64, 8, 8, 16, 32, 16, 32, 32, 64];

    private readonly VulkanDevice _vulkan;
    private readonly Swapchain? _swapchain;
    private readonly Semaphore _acquired;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly Pipeline _rasterize;
    private readonly Pipeline _finishRendering;
    private readonly Pipeline _drawFrame;
    private readonly Buffer _renderingBuffer;
    private readonly DeviceMemory _renderingMemory;
    private readonly DescriptorPool _descriptorPool;
    private readonly DescriptorSet _descriptorSet;
    private readonly CommandPool _commandPool;
    private readonly CommandBuffer _commandBuffer;
    private readonly Fence _fence;
    private readonly Lock _submittedLock = new();
    private readonly IRenderHooks? _hooks;
    private readonly bool[] _mainScreenPositions = new bool[3];

    private byte[] _frame = new byte[SmallestFrameSize];
    private int _frameSize;

    private Buffer _frameBuffer;
    private DeviceMemory _frameMemory;
    private Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private byte* _staging;
    private int _frameCapacity;

    private Image _output;
    private DeviceMemory _outputMemory;
    private ImageView _outputView;
    private uint _outputWidth;
    private uint _outputHeight;
    private bool _hooksInitialized;

    private volatile bool _hasSubmittedFrame;
    private bool _hasNewFrame;
    private ScreenSizing _submittedSizing;
    private ViewMargins _submittedMargins;
    private float _submittedScroll;
    private ScreenSizing _sizing;
    private ViewMargins _margins;
    private float _scroll;

    /// <summary>Creates a renderer that draws into a window.</summary>
    /// <param name="instanceExtensions">The Vulkan instance extensions that the window needs, like the ones <c>SDL_Vulkan_GetInstanceExtensions</c> gives you.</param>
    /// <param name="createSurface">Creates the window's <c>VkSurfaceKHR</c> for the <c>VkInstance</c> that you're given.</param>
    /// <param name="hooks">Your own drawing on top of the game, like a user interface.</param>
    public FrameRenderer(IReadOnlyList<string> instanceExtensions, Func<nint, ulong> createSurface, IRenderHooks? hooks = null)
        : this(new VulkanDevice(instanceExtensions, createSurface, hooks))
    {
        _hooks = hooks;
        _swapchain = new Swapchain(_vulkan);
        var info = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        VulkanDevice.Check(_vulkan.Api.CreateSemaphore(_vulkan.Device, in info, null, out _acquired), "vkCreateSemaphore");
    }

    /// <summary>Creates a renderer without a window. Keep in mind that you can only use <see cref="Render"/> and <see cref="TakeScreenshot"/> with it.</summary>
    public FrameRenderer()
        : this(new VulkanDevice([], null, null))
    {
    }

    private FrameRenderer(VulkanDevice vulkan)
    {
        _vulkan = vulkan;
        _descriptorSetLayout = CreateDescriptorSetLayout();
        _pipelineLayout = CreatePipelineLayout();
        _rasterize = CreatePipeline("Rasterize");
        _finishRendering = CreatePipeline("FinishRendering");
        _drawFrame = CreatePipeline("DrawFrame");
        _descriptorPool = CreateDescriptorPool();
        _descriptorSet = AllocateDescriptorSet();
        _commandPool = CreateCommandPool();
        _commandBuffer = AllocateCommandBuffer();
        _fence = CreateFence();
        _renderingBuffer = CreateBuffer(RenderingSize, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit, out _renderingMemory);
        BindStorageBuffer(2, _renderingBuffer);
        CreateBuffers(SmallestFrameSize);
    }

    /// <summary>
    /// Gives the renderer the frame that the game just finished, along with which screens to show and what goes past
    /// their edges. Call it from <see cref="Video.FrameFinished"/>.
    /// <para>
    /// <paramref name="scroll"/> is how many lines down <see cref="ScreenSizing.Scrolled"/> and
    /// <see cref="ScreenSizing.Cropped"/> start. It doesn't have to be a whole number, so a slow scroll still moves
    /// smoothly in a big window. <paramref name="insets"/> are drawn on top of the picture.
    /// </para>
    /// </summary>
    public void Submit(VideoFrame frame, ScreenSizing sizing, FrameMargins margins, float scroll = 0, FrameInsets? insets = null)
    {
        lock (_submittedLock)
        {
            var rendering = frame.Rendering;
            var polygons = YSorting.Sort(rendering.Polygons, rendering.Vertices, rendering.SwapParameters);
            List<SortedPolygon> marginPolygons = margins.Width == 0 ? [] : YSorting.Sort(rendering.MarginPolygons, rendering.MarginVertices, rendering.SwapParameters);
            int copiesOffset = LayoutsOffset + (frame.LayoutCount * LayoutSize);
            int polygonsOffset = copiesOffset + (frame.CopyCount * VideoFrame.KilobyteSize);
            int marginPolygonsOffset = polygonsOffset + (polygons.Count * PolygonWords * sizeof(uint));
            int size = marginPolygonsOffset + (marginPolygons.Count * PolygonWords * sizeof(uint));
            if (_frame.Length < size)
            {
                _frame = new byte[size];
            }

            var block = _frame.AsSpan(0, size);
            MemoryMarshal.AsBytes<uint>(frame.Lines).CopyTo(block);

            var layoutOfLine = MemoryMarshal.Cast<byte, uint>(block.Slice(LayoutOfLineOffset, Video.VisibleLines * sizeof(uint)));
            for (int line = 0; line < Video.VisibleLines; line++)
            {
                layoutOfLine[line] = frame.LayoutOfLine[line];
            }

            var objectLines = MemoryMarshal.Cast<byte, uint>(block[ObjectLinesOffset..SuppliedColumnsOffset]);
            FindObjectLines((line, engine) => frame.OnLine(line, OAMKilobyte + engine), objectLines);

            MemoryMarshal.AsBytes<uint>(margins.SuppliedColumns).CopyTo(block[SuppliedColumnsOffset..]);
            MemoryMarshal.AsBytes<ushort>(margins.Tiles).CopyTo(block[MarginTilesOffset..]);
            MemoryMarshal.AsBytes<uint>(margins.WindowEdges).CopyTo(block[MarginWindowsOffset..]);
            var insetWords = MemoryMarshal.Cast<byte, uint>(block.Slice(InsetsOffset, FrameInsets.Words * sizeof(uint)));
            if (insets is null)
            {
                insetWords.Clear();
            }
            else
            {
                insets.CopyTo(insetWords);
            }
            bool isEngineAOnTop = (frame.Lines[^1] & DisplaySwap) != 0;
            (_submittedSizing, _submittedMargins) = (Resolve(sizing, _mainScreenPositions, isEngineAOnTop), new ViewMargins((uint)margins.Width, (uint)margins.CroppedWidth));
            _submittedScroll = Math.Clamp(scroll, 0, Height - (int)ViewSize(_submittedSizing, _submittedMargins).Height);

            var header = MemoryMarshal.Cast<byte, uint>(block.Slice(RenderingOffset, RenderingHeaderWords * sizeof(uint)));
            header[0] = rendering.DISP3DCNT | ((rendering.SwapParameters ?? 0) << 16);
            header[1] = (uint)polygons.Count;
            header[2] = (uint)(polygonsOffset / sizeof(uint));
            header[3] = (uint)marginPolygons.Count;
            header[4] = (uint)(marginPolygonsOffset / sizeof(uint));
            rendering.ControlRegisters.CopyTo(block[ControlRegistersOffset..]);

            BinaryPrimitives.WriteUInt32LittleEndian(block[CopiesStartOffset..], (uint)(copiesOffset / sizeof(uint)));
            MemoryMarshal.AsBytes(frame.Layouts.AsSpan(0, frame.LayoutCount * VideoFrame.VideoMemoryKilobytes)).CopyTo(block[LayoutsOffset..]);
            frame.Copies.AsSpan(0, frame.CopyCount * VideoFrame.KilobyteSize).CopyTo(block[copiesOffset..]);
            WritePolygons(polygons, rendering.Vertices, MemoryMarshal.Cast<byte, uint>(block[polygonsOffset..]));
            WritePolygons(marginPolygons, rendering.MarginVertices, MemoryMarshal.Cast<byte, uint>(block[marginPolygonsOffset..]));

            _frameSize = size;
            _hasSubmittedFrame = true;
            _hasNewFrame = true;
        }
    }

    /// <summary>
    /// Shows the latest frame in the window, centered and as big as it fits, with black around it. Keep in mind that
    /// it waits until the display is ready for another picture, so you only need to call it once per refresh.
    /// </summary>
    public void Present(int width, int height)
    {
        if (_swapchain is null)
        {
            throw new InvalidOperationException("This renderer has no window to show frames in.");
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        WaitForGPU();
        if (!_swapchain.TryAcquire((uint)width, (uint)height, _acquired, out uint index))
        {
            return;
        }

        var target = _swapchain.ImageAt(index);
        BeginRecording();
        ClearTarget(target);
        if (_hasSubmittedFrame)
        {
            Blit(target, RenderFrame(_swapchain.Width, _swapchain.Height));
        }

        if (_hooks is null)
        {
            Barrier(target, ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr, AccessFlags.TransferWriteBit, 0, PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit);
        }
        else
        {
            DrawHooks(_hooks, target, index);
        }

        EndRecording();
        SubmitCommands(_acquired, _swapchain.RenderedAt(index));
        _swapchain.Present(index);
    }

    /// <summary>Draws the latest frame the same way <see cref="Present"/> would for a window of that size, but doesn't show it.</summary>
    public void Render(int width, int height)
    {
        WaitForGPU();
        BeginRecording();
        if (_hasSubmittedFrame)
        {
            RenderFrame((uint)width, (uint)height);
        }

        EndRecording();
        SubmitCommands(default, default);
    }

    /// <summary>
    /// Finds which pixel of the screens is under a point in a window of the given size, going by where
    /// <see cref="Present"/> put them last time. This is how you turn a mouse click into a touch on the touchscreen.
    /// The top screen's pixels are in rows 0 to 191, and the bottom screen's are in rows 192 to 383. Returns
    /// <c>false</c> if the point isn't on either screen.
    /// </summary>
    public bool TryFindPixel(float windowX, float windowY, int windowWidth, int windowHeight, out int x, out int y)
    {
        return FindPixel(windowX, windowY, windowWidth, windowHeight, _sizing, _margins, _scroll, out x, out y);
    }

    internal static bool FindPixel(float windowX, float windowY, int windowWidth, int windowHeight, ScreenSizing sizing, ViewMargins margins, float scroll, out int x, out int y)
    {
        (x, y) = (0, 0);
        var (viewWidth, viewHeight) = ViewSize(sizing, margins);
        var picture = Fit((uint)Math.Max(windowWidth, 0), (uint)Math.Max(windowHeight, 0), viewWidth, viewHeight);
        if (picture.Extent.Width == 0 || picture.Extent.Height == 0)
        {
            return false;
        }

        float column = ((windowX - picture.Offset.X) * viewWidth / picture.Extent.Width) - ShownMargin(sizing, margins);
        float row = (windowY - picture.Offset.Y) * viewHeight / picture.Extent.Height;
        if (column < 0 || column >= Width || row < 0 || row >= viewHeight)
        {
            return false;
        }

        float firstRow = sizing switch
        {
            ScreenSizing.BottomOnly => Video.VisibleLines,
            ScreenSizing.Scrolled or ScreenSizing.Cropped => scroll,
            _ => 0,
        };

        (x, y) = ((int)column, (int)(row + firstRow));
        return true;
    }

    /// <summary>Returns the last picture that was drawn, before it got scaled to fit the window. Returns null if nothing has been drawn yet.</summary>
    public Screenshot? TakeScreenshot()
    {
        if (_output.Handle == 0)
        {
            return null;
        }

        int size = (int)(_outputWidth * _outputHeight * 4);
        var buffer = CreateBuffer(size, BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var memory);
        try
        {
            WaitForGPU();
            BeginRecording();
            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D(_outputWidth, _outputHeight, 1),
            };
            _vulkan.Api.CmdCopyImageToBuffer(_commandBuffer, _output, ImageLayout.TransferSrcOptimal, buffer, 1, &region);
            EndRecording();

            SubmitCommands(default, default);
            WaitForGPU();

            void* mapped;
            VulkanDevice.Check(_vulkan.Api.MapMemory(_vulkan.Device, memory, 0, (ulong)size, 0, &mapped), "vkMapMemory");
            byte[] pixels = new ReadOnlySpan<byte>(mapped, size).ToArray();
            _vulkan.Api.UnmapMemory(_vulkan.Device, memory);
            return new Screenshot((int)_outputWidth, (int)_outputHeight, pixels);
        }
        finally
        {
            _vulkan.Api.DestroyBuffer(_vulkan.Device, buffer, null);
            _vulkan.Api.FreeMemory(_vulkan.Device, memory, null);
        }
    }

    public void Dispose()
    {
        _vulkan.Api.DeviceWaitIdle(_vulkan.Device);
        if (_hooksInitialized)
        {
            _hooks!.Deinit();
        }

        _swapchain?.Dispose();
        _vulkan.Api.DestroySemaphore(_vulkan.Device, _acquired, null);
        DestroyOutput();
        DestroyBuffers();
        _vulkan.Api.DestroyFence(_vulkan.Device, _fence, null);
        _vulkan.Api.DestroyCommandPool(_vulkan.Device, _commandPool, null);
        _vulkan.Api.DestroyDescriptorPool(_vulkan.Device, _descriptorPool, null);
        _vulkan.Api.DestroyBuffer(_vulkan.Device, _renderingBuffer, null);
        _vulkan.Api.FreeMemory(_vulkan.Device, _renderingMemory, null);
        _vulkan.Api.DestroyPipeline(_vulkan.Device, _rasterize, null);
        _vulkan.Api.DestroyPipeline(_vulkan.Device, _finishRendering, null);
        _vulkan.Api.DestroyPipeline(_vulkan.Device, _drawFrame, null);
        _vulkan.Api.DestroyPipelineLayout(_vulkan.Device, _pipelineLayout, null);
        _vulkan.Api.DestroyDescriptorSetLayout(_vulkan.Device, _descriptorSetLayout, null);
        _vulkan.Dispose();
    }

    internal delegate ReadOnlySpan<byte> OAMOfLine(int line, int engine);

    internal static void FindObjectLines(OAMOfLine oamOfLine, Span<uint> objectLines)
    {
        objectLines.Clear();
        for (int line = 0; line < Video.VisibleLines; line++)
        {
            for (int engine = 0; engine < Engines; engine++)
            {
                var oam = oamOfLine(line, engine);
                for (int n = 0; n < ObjectCount; n++)
                {
                    ushort attribute0 = BinaryPrimitives.ReadUInt16LittleEndian(oam[(n * 8)..]);
                    ushort attribute1 = BinaryPrimitives.ReadUInt16LittleEndian(oam[((n * 8) + 2)..]);
                    bool isAffine = (attribute0 & 0x100) != 0;
                    bool isDisabledOrDoubleSize = (attribute0 & 0x200) != 0;
                    int shape = attribute0 >> 14;
                    if ((!isAffine && isDisabledOrDoubleSize) || shape == 3)
                    {
                        continue;
                    }

                    int height = ObjectHeights[(shape * 4) + (attribute1 >> 14)] * (isAffine && isDisabledOrDoubleSize ? 2 : 1);
                    if (((line - (attribute0 & 0xFF)) & 0xFF) < height)
                    {
                        objectLines[(((line * Engines) + engine) * ObjectGroups) + (n / 32)] |= 1u << (n % 32);
                    }
                }
            }
        }
    }

    private static void WritePolygons(List<SortedPolygon> polygons, IReadOnlyList<GeometryEngine.Vertex> vertices, Span<uint> destination)
    {
        for (int i = 0; i < polygons.Count; i++)
        {
            var ordered = polygons[i];
            var polygon = ordered.Polygon;
            var words = destination.Slice(i * PolygonWords, PolygonWords);
            words.Clear();

            bool isShadowMask = (polygon.Attributes & (PolygonID | ShadowMode)) == ShadowMode;
            bool isShadow = (polygon.Attributes & ShadowMode) == ShadowMode && !isShadowMask;
            words[0] = polygon.Attributes;
            words[1] = polygon.TextureParameters;
            words[2] = polygon.PaletteBase;
            words[3] = (uint)polygon.Vertices.Length | (polygon.IsFrontFacing ? FacingFlag : 0) | (ordered.IsDot ? DotFlag : 0) | (isShadowMask ? ShadowMaskFlag : 0) | (isShadow ? ShadowFlag : 0)
                | ((uint)ordered.TopVertex << 8) | ((uint)ordered.BottomVertex << 12);
            words[4] = (uint)ordered.Top | ((uint)ordered.Bottom << 16);

            for (int v = 0; v < polygon.Vertices.Length; v++)
            {
                var vertex = vertices[polygon.Vertices[v]];
                var vertexWords = words.Slice(PolygonHeaderWords + (v * VertexWords), VertexWords);
                vertexWords[0] = (ushort)vertex.ScreenX | ((uint)vertex.ScreenY << 16);
                vertexWords[1] = (uint)polygon.Depths[v];
                vertexWords[2] = (uint)polygon.NormalizedWs[v];
                vertexWords[3] = FinalColor(vertex.Red) | (FinalColor(vertex.Green) << 10) | (FinalColor(vertex.Blue) << 20);
                vertexWords[4] = (ushort)vertex.S | ((uint)(ushort)vertex.T << 16);
            }
        }
    }

    private static uint FinalColor(byte color)
    {
        uint color5 = (uint)color >> 1;
        return color5 == 0 ? 0 : (color5 << FinalColorShift) + FinalColorFraction;
    }

    private Rect2D RenderFrame(uint targetWidth, uint targetHeight)
    {
        bool uploaded = TryUpload();

        var (viewWidth, viewHeight) = ViewSize(_sizing, _margins);
        var destination = Fit(targetWidth, targetHeight, viewWidth, viewHeight);
        uint scale = Math.Clamp(destination.Extent.Height / viewHeight, 1, MaxScale);
        bool outputChanged = EnsureOutput(viewWidth * scale, viewHeight * scale);

        if (uploaded || outputChanged)
        {
            RunShader(new DrawParameters { Scale = scale, Margin = ShownMargin(_sizing, _margins), Sizing = (uint)_sizing, Scroll = (uint)MathF.Round(_scroll * scale) }, outputChanged);
        }

        return destination;
    }

    internal static ScreenSizing Resolve(ScreenSizing sizing, bool[] mainScreenPositions, bool isEngineAOnTop)
    {
        (mainScreenPositions[2], mainScreenPositions[1], mainScreenPositions[0]) = (mainScreenPositions[1], mainScreenPositions[0], isEngineAOnTop);
        if (sizing != ScreenSizing.Auto)
        {
            return sizing;
        }

        if (mainScreenPositions[0] == mainScreenPositions[2] && mainScreenPositions[0] != mainScreenPositions[1])
        {
            return ScreenSizing.Even;
        }

        return mainScreenPositions[0] ? ScreenSizing.TopOnly : ScreenSizing.BottomOnly;
    }

    private static (uint Width, uint Height) ViewSize(ScreenSizing sizing, ViewMargins margins)
    {
        return sizing switch
        {
            ScreenSizing.Even => (Width + (2 * margins.Width), Height),
            ScreenSizing.Cropped => (Width + (2 * margins.Cropped), (uint)FrameMargins.LinesCroppedFor((int)margins.Width, (int)margins.Cropped)),
            _ => (Width + (2 * margins.Width), Video.VisibleLines),
        };
    }

    private static uint ShownMargin(ScreenSizing sizing, ViewMargins margins) => sizing == ScreenSizing.Cropped ? margins.Cropped : margins.Width;

    private static Rect2D Fit(uint targetWidth, uint targetHeight, uint viewWidth, uint viewHeight)
    {
        uint width = Math.Min(targetWidth, targetHeight * viewWidth / viewHeight);
        uint height = width * viewHeight / viewWidth;
        return new Rect2D(new Offset2D((int)(targetWidth - width) / 2, (int)(targetHeight - height) / 2), new Extent2D(width, height));
    }

    private void Blit(Image target, Rect2D destination)
    {
        var blit = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
        };
        blit.SrcOffsets[1] = new Offset3D((int)_outputWidth, (int)_outputHeight, 1);
        blit.DstOffsets[0] = new Offset3D(destination.Offset.X, destination.Offset.Y, 0);
        blit.DstOffsets[1] = new Offset3D(destination.Offset.X + (int)destination.Extent.Width, destination.Offset.Y + (int)destination.Extent.Height, 1);

        var filter = destination.Extent.Height == _outputHeight ? Filter.Nearest : Filter.Linear;
        _vulkan.Api.CmdBlitImage(_commandBuffer, _output, ImageLayout.TransferSrcOptimal, target, ImageLayout.TransferDstOptimal, 1, &blit, filter);
    }

    private void DrawHooks(IRenderHooks hooks, Image target, uint index)
    {
        if (!_hooksInitialized)
        {
            hooks.Init(new RenderDevice(_vulkan.Api, _vulkan.PhysicalDevice, _vulkan.Device, _vulkan.Queue, _vulkan.QueueFamily));
            _hooksInitialized = true;
        }

        var extent = new Extent2D(_swapchain!.Width, _swapchain.Height);
        hooks.Draw(_commandBuffer, new RenderTarget(target, _swapchain.ViewAt(index), _swapchain.Format, extent, ImageLayout.TransferDstOptimal));
    }

    private void ClearTarget(Image target)
    {
        Barrier(target, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 0, AccessFlags.TransferWriteBit, PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit);
        var black = new ClearColorValue { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 };
        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        _vulkan.Api.CmdClearColorImage(_commandBuffer, target, ImageLayout.TransferDstOptimal, &black, 1, &range);
        Barrier(target, ImageLayout.TransferDstOptimal, ImageLayout.TransferDstOptimal, AccessFlags.TransferWriteBit, AccessFlags.TransferWriteBit, PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit);
    }

    private bool TryUpload()
    {
        int size;
        lock (_submittedLock)
        {
            if (!_hasNewFrame)
            {
                return false;
            }

            size = _frameSize;
            if (size > _frameCapacity)
            {
                DestroyBuffers();
                CreateBuffers(size * 2);
            }

            _frame.AsSpan(0, size).CopyTo(new Span<byte>(_staging, _frameCapacity));
            _hasNewFrame = false;
            (_sizing, _margins, _scroll) = (_submittedSizing, _submittedMargins, _submittedScroll);
        }

        var copy = new BufferCopy(0, 0, (ulong)size);
        _vulkan.Api.CmdCopyBuffer(_commandBuffer, _stagingBuffer, _frameBuffer, 1, &copy);
        var barrier = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = _frameBuffer,
            Size = Vk.WholeSize,
        };
        _vulkan.Api.CmdPipelineBarrier(_commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 1, &barrier, 0, null);
        return true;
    }

    private void RunShader(DrawParameters parameters, bool isNewOutput)
    {
        var oldLayout = isNewOutput ? ImageLayout.Undefined : ImageLayout.TransferSrcOptimal;
        Barrier(_output, oldLayout, ImageLayout.General, AccessFlags.TransferReadBit, AccessFlags.ShaderWriteBit, PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit);

        var descriptorSet = _descriptorSet;
        _vulkan.Api.CmdBindDescriptorSets(_commandBuffer, PipelineBindPoint.Compute, _pipelineLayout, 0, 1, &descriptorSet, 0, null);
        uint[] windows = parameters.Margin == 0 ? [ScreenWindow] : [ScreenWindow, LeftWindow, RightWindow];

        _vulkan.Api.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Compute, _rasterize);
        foreach (uint window in windows)
        {
            PushParameters(parameters with { Window = window });
            _vulkan.Api.CmdDispatch(_commandBuffer, 1, Video.VisibleLines, 1);
        }

        RenderingBarrier();

        _vulkan.Api.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Compute, _finishRendering);
        foreach (uint window in windows)
        {
            PushParameters(parameters with { Window = window });
            _vulkan.Api.CmdDispatch(_commandBuffer, Width / FinishGroupSize, Video.VisibleLines / FinishGroupSize, 1);
        }

        RenderingBarrier();

        PushParameters(parameters);
        _vulkan.Api.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Compute, _drawFrame);
        _vulkan.Api.CmdDispatch(
            _commandBuffer,
            (_outputWidth + ThreadGroupSize - 1) / ThreadGroupSize,
            (_outputHeight + ThreadGroupSize - 1) / ThreadGroupSize,
            1);

        Barrier(_output, ImageLayout.General, ImageLayout.TransferSrcOptimal, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
    }

    private void PushParameters(DrawParameters parameters)
    {
        _vulkan.Api.CmdPushConstants(_commandBuffer, _pipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(DrawParameters), &parameters);
    }

    private void RenderingBarrier()
    {
        var barrier = new BufferMemoryBarrier
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = _renderingBuffer,
            Size = Vk.WholeSize,
        };
        _vulkan.Api.CmdPipelineBarrier(_commandBuffer, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 1, &barrier, 0, null);
    }

    private bool EnsureOutput(uint width, uint height)
    {
        if (width == _outputWidth && height == _outputHeight)
        {
            return false;
        }

        DestroyOutput();

        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(_vulkan.Api.CreateImage(_vulkan.Device, in info, null, out _output), "vkCreateImage");

        _vulkan.Api.GetImageMemoryRequirements(_vulkan.Device, _output, out var requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _vulkan.FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(_vulkan.Api.AllocateMemory(_vulkan.Device, in allocation, null, out _outputMemory), "vkAllocateMemory");
        VulkanDevice.Check(_vulkan.Api.BindImageMemory(_vulkan.Device, _output, _outputMemory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _output,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(_vulkan.Api.CreateImageView(_vulkan.Device, in viewInfo, null, out _outputView), "vkCreateImageView");

        var imageInfo = new DescriptorImageInfo { ImageView = _outputView, ImageLayout = ImageLayout.General };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 1,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageImage,
            PImageInfo = &imageInfo,
        };
        _vulkan.Api.UpdateDescriptorSets(_vulkan.Device, 1, &write, 0, null);

        _outputWidth = width;
        _outputHeight = height;
        return true;
    }

    private void DestroyOutput()
    {
        _vulkan.Api.DestroyImageView(_vulkan.Device, _outputView, null);
        _vulkan.Api.DestroyImage(_vulkan.Device, _output, null);
        _vulkan.Api.FreeMemory(_vulkan.Device, _outputMemory, null);
        (_output, _outputMemory, _outputView) = (default, default, default);
    }

    private void CreateBuffers(int frameCapacity)
    {
        _frameBuffer = CreateBuffer(frameCapacity, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, out _frameMemory);
        _stagingBuffer = CreateBuffer(frameCapacity, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out _stagingMemory);

        void* staging;
        VulkanDevice.Check(_vulkan.Api.MapMemory(_vulkan.Device, _stagingMemory, 0, (ulong)frameCapacity, 0, &staging), "vkMapMemory");
        _staging = (byte*)staging;
        _frameCapacity = frameCapacity;
        BindStorageBuffer(0, _frameBuffer);
    }

    private void BindStorageBuffer(uint binding, Buffer buffer)
    {
        var bufferInfo = new DescriptorBufferInfo { Buffer = buffer, Range = Vk.WholeSize };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo,
        };
        _vulkan.Api.UpdateDescriptorSets(_vulkan.Device, 1, &write, 0, null);
    }

    private void DestroyBuffers()
    {
        _vulkan.Api.DestroyBuffer(_vulkan.Device, _stagingBuffer, null);
        _vulkan.Api.FreeMemory(_vulkan.Device, _stagingMemory, null);
        _vulkan.Api.DestroyBuffer(_vulkan.Device, _frameBuffer, null);
        _vulkan.Api.FreeMemory(_vulkan.Device, _frameMemory, null);
    }

    private Buffer CreateBuffer(int size, BufferUsageFlags usage, MemoryPropertyFlags properties, out DeviceMemory memory)
    {
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = (ulong)size, Usage = usage, SharingMode = SharingMode.Exclusive };
        VulkanDevice.Check(_vulkan.Api.CreateBuffer(_vulkan.Device, in info, null, out var buffer), "vkCreateBuffer");

        _vulkan.Api.GetBufferMemoryRequirements(_vulkan.Device, buffer, out var requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _vulkan.FindMemoryType(requirements.MemoryTypeBits, properties),
        };
        VulkanDevice.Check(_vulkan.Api.AllocateMemory(_vulkan.Device, in allocation, null, out memory), "vkAllocateMemory");
        VulkanDevice.Check(_vulkan.Api.BindBufferMemory(_vulkan.Device, buffer, memory, 0), "vkBindBufferMemory");
        return buffer;
    }

    private void Barrier(Image image, ImageLayout oldLayout, ImageLayout newLayout, AccessFlags srcAccess, AccessFlags dstAccess, PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        _vulkan.Api.CmdPipelineBarrier(_commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private void WaitForGPU()
    {
        var fence = _fence;
        VulkanDevice.Check(_vulkan.Api.WaitForFences(_vulkan.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
    }

    private void BeginRecording()
    {
        var fence = _fence;
        VulkanDevice.Check(_vulkan.Api.ResetFences(_vulkan.Device, 1, &fence), "vkResetFences");
        VulkanDevice.Check(_vulkan.Api.ResetCommandBuffer(_commandBuffer, 0), "vkResetCommandBuffer");
        var info = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        VulkanDevice.Check(_vulkan.Api.BeginCommandBuffer(_commandBuffer, in info), "vkBeginCommandBuffer");
    }

    private void SubmitCommands(Semaphore wait, Semaphore signal)
    {
        var commandBuffer = _commandBuffer;
        var waitStage = PipelineStageFlags.TransferBit;
        var info = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = wait.Handle == 0 ? 0u : 1,
            PWaitSemaphores = &wait,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = signal.Handle == 0 ? 0u : 1,
            PSignalSemaphores = &signal,
        };
        VulkanDevice.Check(_vulkan.Api.QueueSubmit(_vulkan.Queue, 1, &info, _fence), "vkQueueSubmit");
    }

    private void EndRecording()
    {
        VulkanDevice.Check(_vulkan.Api.EndCommandBuffer(_commandBuffer), "vkEndCommandBuffer");
    }

    private DescriptorSetLayout CreateDescriptorSetLayout()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[]
        {
            new() { Binding = 0, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit },
            new() { Binding = 1, DescriptorType = DescriptorType.StorageImage, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit },
            new() { Binding = 2, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit },
        };
        var info = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 3, PBindings = bindings };
        VulkanDevice.Check(_vulkan.Api.CreateDescriptorSetLayout(_vulkan.Device, in info, null, out var layout), "vkCreateDescriptorSetLayout");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        var descriptorSetLayout = _descriptorSetLayout;
        var pushConstants = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(DrawParameters));
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorSetLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstants,
        };
        VulkanDevice.Check(_vulkan.Api.CreatePipelineLayout(_vulkan.Device, in info, null, out var layout), "vkCreatePipelineLayout");
        return layout;
    }

    private Pipeline CreatePipeline(string entryPoint)
    {
        using var stream = typeof(FrameRenderer).Assembly.GetManifestResourceStream($"{entryPoint}.spv")!;
        byte[] code = new byte[stream.Length];
        stream.ReadExactly(code);

        fixed (byte* codePointer = code)
        fixed (byte* main = "main\0"u8)
        {
            var moduleInfo = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)codePointer };
            VulkanDevice.Check(_vulkan.Api.CreateShaderModule(_vulkan.Device, in moduleInfo, null, out var module), "vkCreateShaderModule");
            try
            {
                var info = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = module,
                        PName = main,
                    },
                    Layout = _pipelineLayout,
                };
                VulkanDevice.Check(_vulkan.Api.CreateComputePipelines(_vulkan.Device, default, 1, in info, null, out var pipeline), "vkCreateComputePipelines");
                return pipeline;
            }
            finally
            {
                _vulkan.Api.DestroyShaderModule(_vulkan.Device, module, null);
            }
        }
    }

    private DescriptorPool CreateDescriptorPool()
    {
        var sizes = stackalloc DescriptorPoolSize[]
        {
            new(DescriptorType.StorageBuffer, 2),
            new(DescriptorType.StorageImage, 1),
        };
        var info = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 2, PPoolSizes = sizes };
        VulkanDevice.Check(_vulkan.Api.CreateDescriptorPool(_vulkan.Device, in info, null, out var pool), "vkCreateDescriptorPool");
        return pool;
    }

    private DescriptorSet AllocateDescriptorSet()
    {
        var layout = _descriptorSetLayout;
        var info = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _descriptorPool, DescriptorSetCount = 1, PSetLayouts = &layout };
        VulkanDevice.Check(_vulkan.Api.AllocateDescriptorSets(_vulkan.Device, in info, out var set), "vkAllocateDescriptorSets");
        return set;
    }

    private CommandPool CreateCommandPool()
    {
        var info = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _vulkan.QueueFamily,
        };
        VulkanDevice.Check(_vulkan.Api.CreateCommandPool(_vulkan.Device, in info, null, out var pool), "vkCreateCommandPool");
        return pool;
    }

    private CommandBuffer AllocateCommandBuffer()
    {
        var info = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VulkanDevice.Check(_vulkan.Api.AllocateCommandBuffers(_vulkan.Device, in info, out var commandBuffer), "vkAllocateCommandBuffers");
        return commandBuffer;
    }

    private Fence CreateFence()
    {
        var info = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        VulkanDevice.Check(_vulkan.Api.CreateFence(_vulkan.Device, in info, null, out var fence), "vkCreateFence");
        return fence;
    }

    internal readonly record struct ViewMargins(uint Width, uint Cropped);

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawParameters
    {
        public uint Scale;
        public uint Margin;
        public uint Sizing;
        public uint Window;
        public uint Scroll;
    }
}
