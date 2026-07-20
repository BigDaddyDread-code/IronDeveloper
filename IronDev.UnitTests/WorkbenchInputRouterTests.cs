using IronDev.Core.Workbench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class WorkbenchInputRouterTests
{
    [DataTestMethod]
    [DataRow("/help", WorkbenchInputKinds.Help, "/help", null)]
    [DataRow("  /HELP\r\n", WorkbenchInputKinds.Help, "/help", null)]
    [DataRow("\t/TICKET", WorkbenchInputKinds.Ticket, "/ticket", null)]
    [DataRow(" /TiCkEt   login flow ", WorkbenchInputKinds.Ticket, "/ticket", "login flow")]
    public void Parse_RecognizesOnlyTheAllowlistedCommands(
        string input,
        string expectedKind,
        string expectedCommand,
        string? expectedInstruction)
    {
        var route = WorkbenchInputRouter.Parse(input);

        Assert.AreEqual(expectedKind, route.Kind);
        Assert.AreEqual(expectedCommand, route.NormalizedCommand);
        Assert.AreEqual(expectedInstruction, route.Instruction);
        Assert.IsTrue(route.IsCommand);
    }

    [DataTestMethod]
    [DataRow("We may use /ticket later.")]
    [DataRow("create tickets from this idea")]
    [DataRow("hello/help")]
    [DataRow("  ordinary shaping conversation")]
    [DataRow("")]
    [DataRow("   \r\n")]
    public void Parse_LeavesOrdinaryProseOnTheConversationRoute(string input)
    {
        var route = WorkbenchInputRouter.Parse(input);

        Assert.AreEqual(WorkbenchInputKinds.Conversation, route.Kind);
        Assert.IsFalse(route.IsCommand);
        Assert.IsNull(route.RawCommandToken);
        Assert.IsNull(route.NormalizedCommand);
        Assert.IsNull(route.Instruction);
    }

    [TestMethod]
    public void Parse_RejectsUnknownTokenWithoutRetainingTheFullComposerText()
    {
        var route = WorkbenchInputRouter.Parse("  /tickte keep this private instruction client-side");

        Assert.AreEqual(WorkbenchInputKinds.CommandRejected, route.Kind);
        Assert.AreEqual("/tickte", route.RawCommandToken);
        Assert.IsNull(route.NormalizedCommand);
        Assert.AreEqual("keep this private instruction client-side", route.Instruction);
    }

    [TestMethod]
    public void Parse_DoesNotSupportAliases()
    {
        foreach (var alias in new[] { "/h", "/tickets", "/new-ticket", "/?" })
        {
            var route = WorkbenchInputRouter.Parse(alias);
            Assert.AreEqual(WorkbenchInputKinds.CommandRejected, route.Kind, alias);
        }
    }
}
