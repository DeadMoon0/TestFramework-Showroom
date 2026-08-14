using Xunit;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - FACILITY RULES, POSTED BY ORDER OF SOMEBODY
//
//  Modules in this wing run one at a time. This is not a performance oversight and
//  we would appreciate it not being reported as one again.
//
//  Two reasons, both learned the direct way:
//
//    1. THE APPLICATION IS BUILT FROM ITS PROJECT. Several modules asking the build
//       system to publish the same project at the same moment produces exactly the
//       outcome you would expect from two people editing the same document with
//       their eyes closed. The build system does not enjoy this. It says so at
//       length, in a temporary directory, and then stops.
//
//    2. CONTAINERS ARE NOT FREE. Each module that asks for the full facility gets a
//       database, a stub and an application. Running six of those concurrently is
//       a stress test of your machine, which is a fine thing to run deliberately
//       and a poor thing to run by accident while trying to learn an API.
//
//  In a real suite you would go further and share one environment across a whole
//  collection, the way the cloud wing does. Here every module stands alone so it
//  can be read alone, and we pay for that in wall-clock time rather than in the
//  reader's attention. That trade is deliberate. The reader is the scarce resource.
// ══════════════════════════════════════════════════════════════════════════════

[assembly: CollectionBehavior(DisableTestParallelization = true)]
