using System.Runtime.CompilerServices;

// The test project exercises internal scoring helpers (band coherence, the surface floor, category parsing) directly —
// these are implementation details of the deterministic fold, not part of the library's public contract (P10).
[assembly: InternalsVisibleTo("Cai.Tests")]
