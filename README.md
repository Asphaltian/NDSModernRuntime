# NDS Modern Runtime

NDS Modern Runtime is what Nintendo DS games recompiled with [NDSRecomp](https://github.com/Asphaltian/NDSRecomp) run on. It takes the place of the DS's hardware and BIOS for the recompiled code of both of its processors, the ARM9 and the ARM7.

## Building

You will need the .NET 10 SDK and [Slang](https://github.com/shader-slang/slang/releases), with `slangc` available on your PATH.

```
git clone --recurse-submodules https://github.com/Asphaltian/NDSModernRuntime
cd NDSModernRuntime
dotnet build
```
