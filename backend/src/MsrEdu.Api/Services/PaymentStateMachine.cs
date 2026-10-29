using MsrEdu.Api.Data;

namespace MsrEdu.Api.Services;

/// <summary>
/// Allowed status transitions for orders and payment transactions. The database
/// enforces the same table with a trigger; keep them in sync.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly Dictionary<PaymentState, PaymentState[]> Next = new()
    {
        [PaymentState.Created] = [PaymentState.Pending, PaymentState.Processing, PaymentState.Successful, PaymentState.Failed, PaymentState.Cancelled, PaymentState.Expired],
        [PaymentState.Pending] = [PaymentState.Processing, PaymentState.Successful, PaymentState.Failed, PaymentState.Cancelled, PaymentState.Expired],
        [PaymentState.Processing] = [PaymentState.Successful, PaymentState.Failed, PaymentState.Cancelled, PaymentState.Expired],
        // A verified payment that lands after the order expired is still honoured (and reconciled).
        [PaymentState.Expired] = [PaymentState.Successful],
        [PaymentState.Successful] = [PaymentState.RefundPending, PaymentState.PartiallyRefunded, PaymentState.Refunded],
        [PaymentState.RefundPending] = [PaymentState.Successful, PaymentState.PartiallyRefunded, PaymentState.Refunded],
        [PaymentState.PartiallyRefunded] = [PaymentState.RefundPending, PaymentState.Refunded],
        [PaymentState.Failed] = [],
        [PaymentState.Cancelled] = [],
        [PaymentState.Refunded] = [],
    };

    public static bool CanTransition(PaymentState from, PaymentState to) => from == to || Next[from].Contains(to);

    public static IEnumerable<(PaymentState From, PaymentState To)> AllTransitions() =>
        Next.SelectMany(kv => kv.Value.Select(to => (kv.Key, to)));
}
