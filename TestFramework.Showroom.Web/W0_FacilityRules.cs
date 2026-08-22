using Xunit;

//doc: Before the web chapters: one rule, and the reasoning behind it, because it is the kind of rule that
//doc: gets reported as a performance oversight if nobody writes down why it exists.
//doc:
//doc: **Chapters in this lane run one at a time.** Two reasons, both learned the direct way.
//doc:
//doc: 1. The application is built from its project. Several chapters asking the build system to publish the
//doc:    same project at the same moment produces exactly the outcome you would expect from two people
//doc:    editing one document with their eyes closed. The build system does not enjoy this. It says so at
//doc:    length, in a temporary directory, and then stops.
//doc: 2. Containers are not free. Each chapter that asks for the full facility gets a database, a stub and
//doc:    an application. Running six of those concurrently is a stress test of your machine - a fine thing
//doc:    to run deliberately, a poor thing to run by accident while trying to learn an API.
//doc:
//doc: In a real suite you would go further and share one environment across a whole collection, the way the
//doc: cloud lane does in chapter A9. Here every chapter stands alone so it can be *read* alone, and we pay
//doc: for that in wall-clock time rather than in the reader's attention. That trade is deliberate. The
//doc: reader is the scarce resource.
//doc:
//doc: One assembly-level attribute does all of it. Note where it applies: this disables xunit's own
//doc: parallelism between test classes, and has nothing to do with the layer-level parallelism inside a
//doc: single run from chapter 13. Two different schedulers, two different problems.

[assembly: CollectionBehavior(DisableTestParallelization = true)]
