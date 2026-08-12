using LockFlow.OrderService.Domain.Common;

namespace LockFlow.OrderService.Domain.Order
{
    public static class OrderErrors
    {
        public static readonly Error NotFound =
            Error.NotFound("Order.NotFound", "The order was not found.");

        public static readonly Error AlreadyPaid =
            Error.Conflict("Order.AlreadyPaid", "The order has already been paid.");

        public static readonly Error InvalidState =
            Error.Validation("Order.InvalidState", "The order state is invalid."); 

        public static readonly Error ArgumentOutOfRange =
            Error.ArgumentOutOfRange("Order.ArgumentOutOfRange", "The order argument out of range.");
    }
}
