using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace LE1GalaxyMapEditor.Tests;

[STATestClass]
public sealed class CategorizedTests
{
    public static IEnumerable<object[]> FastCases => Program.Cases("Fast");
    public static IEnumerable<object[]> IntegrationCases => Program.Cases("Integration");
    public static IEnumerable<object[]> PccCases => Program.Cases("Pcc");
    public static IEnumerable<object[]> WpfCases => Program.Cases("Wpf");

    [TestMethod]
    [DynamicData(nameof(FastCases))]
    [TestCategory("Fast")]
    public void Fast(string name) => Program.Run(name);

    [TestMethod]
    [DynamicData(nameof(IntegrationCases))]
    [TestCategory("Integration")]
    public void Integration(string name) => Program.Run(name);

    [TestMethod]
    [DynamicData(nameof(PccCases))]
    [TestCategory("Pcc")]
    public void Pcc(string name) => Program.Run(name);

    [TestMethod]
    [DynamicData(nameof(WpfCases))]
    [TestCategory("Wpf")]
    public void Wpf(string name) => Program.Run(name);
}
