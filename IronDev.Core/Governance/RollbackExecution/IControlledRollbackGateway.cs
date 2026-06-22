namespace IronDev.Core.Governance.RollbackExecution;

public interface IControlledRollbackGateway
{
    Task<ControlledRollbackReceipt?> ExecuteRollbackAsync(
        ControlledRollbackGatewayRequest request,
        CancellationToken cancellationToken);
}
