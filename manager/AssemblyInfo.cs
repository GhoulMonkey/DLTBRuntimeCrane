// Version resource for CraneManager.exe.
//
// The binary previously shipped with an entirely empty version resource -- no
// company, product, description or version -- which is both a mild heuristic
// signal to scanners and a dead end for any user who right-clicks the file and
// looks at Properties -> Details to decide whether to trust it.
//
// This is the one scanner-facing mitigation available here. Byte-reproducible
// builds are not: the in-box .NET Framework compiler is the legacy C# 5 csc,
// which has no /deterministic switch, so two builds of identical sources differ.
// Getting that would mean depending on the .NET SDK, which defeats the whole
// reason this targets Framework 4.8.

using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CraneManager")]
[assembly: AssemblyDescription("Edits DLTBRuntimeCrane.manifest.json: which Lua scripts DL:TB's Crane runs, and in what order.")]
[assembly: AssemblyProduct("CraneManager")]
// TODO: set this to the Nexus author name the mod is published under.
[assembly: AssemblyCompany("DLTBRuntimeBridge project")]
[assembly: AssemblyCopyright("Provided as-is under the Crane project's terms.")]

// Numeric fields cannot carry a pre-release suffix; the informational version can.
[assembly: AssemblyVersion("2.1.1.0")]
[assembly: AssemblyFileVersion("2.1.1.0")]
[assembly: AssemblyInformationalVersion("2.1.1")]

[assembly: ComVisible(false)]
