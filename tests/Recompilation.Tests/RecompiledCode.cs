using System.Buffers.Binary;
using System.Reflection;
using LibRecomp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NDSRecomp;
using NDSRecomp.ARM;
using NTRModern;
using Processor = NTRModern.Processor;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Recompilation.Tests;

public static class RecompiledCode
{
    public const uint Address = 0x02000000;
    public const uint ReturnAddress = 0x02F00000;
    public const uint Stack = 0x02100000;

    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(RecompContext).Assembly.Location)
            .Append(typeof(Processor).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path))];

    public static RecompFunc Thumb(Processor processor, params (ushort Encoding, string Disassembly)[] code)
    {
        return Recompile(processor, ThumbBytes([.. code.Select(c => c.Encoding)]), isThumb: true, [.. code.Select(c => c.Disassembly)]);
    }

    public static RecompFunc ARM(Processor processor, params (uint Encoding, string Disassembly)[] code)
    {
        return Recompile(processor, ARMBytes([.. code.Select(c => c.Encoding)]), isThumb: false, [.. code.Select(c => c.Disassembly)]);
    }

    public static RecompFunc Recompile(Processor processor, byte[] bytes, bool isThumb, params string[] disassembly)
    {
        var symbol = new Symbol(Address, (uint)bytes.Length, "Test", IsThumb: isThumb, Section: 0, IsFunction: true);
        var context = CreateContext(processor, bytes, Address, [symbol], new InputConfig());
        var analysis = FunctionAnalysis.Analyze(context, context.Functions[0]);
        Assert.Empty(analysis.Errors);

        Assert.Equal(disassembly, analysis.Instructions.Select(i => Disassembler.Format(i)));

        var generator = new CSharpGenerator(context);
        var funcs = Compile(generator.GenerateFile([generator.Generate(analysis)]), generator.GenerateTables()).GetType($"RecompiledFuncs.{processor}.Funcs")!;
        return Function(funcs, "Test");
    }

    internal static Type RecompileProgram(Processor processor, byte[] bytes, uint address, params Symbol[] symbols)
    {
        return RecompileProgram(processor, bytes, address, new InputConfig(), symbols);
    }

    internal static Type RecompileProgram(Processor processor, byte[] bytes, uint address, InputConfig input, params Symbol[] symbols)
    {
        var context = CreateContext(processor, bytes, address, symbols, input);
        var analyses = context.Functions.Select(f => FunctionAnalysis.Analyze(context, f)).ToList();
        Assert.All(analyses, analysis => Assert.Empty(analysis.Errors));

        var generator = new CSharpGenerator(context);
        var funcs = Compile(generator.GenerateFile(analyses.Select(generator.Generate)), generator.GenerateTables()).GetType($"RecompiledFuncs.{processor}.Funcs")!;
        var sections = ((uint, uint, uint, (uint, RecompFunc)[])[])funcs.GetField("Sections")!.GetValue(null)!;
        int[] overlaySections = (int[])funcs.GetField("OverlaySections")!.GetValue(null)!;
        if (processor == Processor.ARM9)
        {
            LibRecomp.ARM9.Recomp.RegisterSections(sections, overlaySections);
        }
        else
        {
            LibRecomp.ARM7.Recomp.RegisterSections(sections, overlaySections);
        }

        return funcs;
    }

    public static RecompFunc Function(Type funcs, string name) => funcs.GetMethod(name)!.CreateDelegate<RecompFunc>();

    public static RecompContext Run(RecompFunc function, RecompContext ctx)
    {
        ctx.R14 = ReturnAddress;
        function(ctx);
        return ctx;
    }

    public static Assembly Compile(params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "RecompiledTest",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + string.Join(Environment.NewLine, sources));

        return Assembly.Load(image.ToArray());
    }

    public static byte[] ThumbBytes(params ushort[] code)
    {
        byte[] bytes = new byte[code.Length * 2];
        for (int i = 0; i < code.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), code[i]);
        }

        return bytes;
    }

    public static byte[] ARMBytes(params uint[] code)
    {
        byte[] bytes = new byte[code.Length * 4];
        for (int i = 0; i < code.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), code[i]);
        }

        return bytes;
    }

    private static Context CreateContext(Processor processor, byte[] bytes, uint address, Symbol[] symbols, InputConfig input)
    {
        var section = new Section(0, "main", address, (uint)bytes.Length, 0);
        var recompiled = processor == Processor.ARM9 ? NDSRecomp.Processor.ARM9 : NDSRecomp.Processor.ARM7;
        return Context.Create(bytes, [section], symbols, [], input, processor: recompiled);
    }
}
