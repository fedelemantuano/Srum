using NUnit.Framework;
using SrumECmd;

namespace SrumTest;

[TestFixture]
public class JsonCommandLineTests
{
    [TestCase("-f input.dat --json output")]
    [TestCase("-f input.dat --csv output")]
    [TestCase("-f input.dat --csv csv-output --json json-output --jsonf srum.json")]
    [TestCase("-d evidence --json output --dt dd/MM/yyyy")]
    public void AcceptsSupportedOutputCombinations(string args)
    {
        Assert.That(Program.CreateRootCommand().Parse(args).Errors, Is.Empty);
    }

    [TestCase("-f input.dat", "Either --csv or --json is required.")]
    [TestCase("--json output", "Either -f or -d is required.")]
    [TestCase("-f input.dat --csv output --jsonf srum.json", "--jsonf requires --json.")]
    [TestCase("-f input.dat --json output --jsonf ../escape.json", "--jsonf must be a valid filename, not a path.")]
    [TestCase("-f input.dat --json output --jsonf C:\\escape.json", "--jsonf must be a valid filename, not a path.")]
    [TestCase("-f input.dat --json output --jsonf NUL.json", "--jsonf must be a valid filename, not a path.")]
    public void RejectsInvalidCombinationsWithActionableErrors(string args, string error)
    {
        Assert.That(Program.CreateRootCommand().Parse(args).Errors, Has.Some.Property("Message").EqualTo(error));
    }

    [Test]
    public void InvalidOutputCombinationReturnsNonzeroExitStatus()
    {
        Assert.That(Program.CreateRootCommand().Parse("-f input.dat --jsonf srum.json").Invoke(), Is.Not.Zero);
    }
}
