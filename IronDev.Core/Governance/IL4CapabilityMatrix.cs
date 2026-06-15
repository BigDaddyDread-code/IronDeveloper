namespace IronDev.Core.Governance;

public interface IL4CapabilityMatrix
{
    IReadOnlyList<L4CapabilityMatrixEntry> List();

    L4CapabilityMatrixEntry Get(string capabilityCode);
}
