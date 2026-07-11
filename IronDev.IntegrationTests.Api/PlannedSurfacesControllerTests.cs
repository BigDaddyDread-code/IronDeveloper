using IronDev.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// AFFORDANCE-1: planned surfaces refuse honestly. Every stub route must return a real
/// 501 with the full refusal envelope — Allowed=false, Reason=NotImplemented, an owning
/// roadmap slice, and a next safe action. A planned surface that returns anything else
/// (or an empty envelope) is a dead end, and a dead end is a UI failure.
/// </summary>
[TestClass]
public sealed class PlannedSurfacesControllerTests
{
    private static PlannedSurfacesController CreateController() =>
        new()
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    [TestMethod]
    public void InviteTenantUser_Refuses501_WithFullEnvelope()
    {
        var result = CreateController().InviteTenantUser(tenantId: 1);
        AssertPlannedEnvelope(result, expectedSliceFragment: "TEAM-0");
    }

    [TestMethod]
    public void InterventionDial_Refuses501_WithFullEnvelope()
    {
        var result = CreateController().GetInterventionDial(projectId: 3);
        AssertPlannedEnvelope(result, expectedSliceFragment: "AUTH-0");
    }

    private static void AssertPlannedEnvelope(IActionResult result, string expectedSliceFragment)
    {
        var objectResult = result as ObjectResult;
        Assert.IsNotNull(objectResult, "A planned surface must return an ObjectResult, never an empty status.");
        Assert.AreEqual(StatusCodes.Status501NotImplemented, objectResult.StatusCode);

        var envelope = objectResult.Value as PlannedSurfaceEnvelope;
        Assert.IsNotNull(envelope, "The 501 must carry the refusal envelope, not an empty body.");
        Assert.IsFalse(envelope.Allowed);
        Assert.AreEqual("NotImplemented", envelope.Reason);
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.Surface), "The envelope must name the surface.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.Detail), "The envelope must say what exists and what is missing.");
        StringAssert.Contains(envelope.PlannedSlice, expectedSliceFragment, "The envelope must name the owning roadmap slice.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.NextSafeAction), "A refusal without a next safe action is a dead end.");
        Assert.AreEqual(PlannedSurfaceEnvelope.BoundaryText, envelope.Boundary);
    }
}
