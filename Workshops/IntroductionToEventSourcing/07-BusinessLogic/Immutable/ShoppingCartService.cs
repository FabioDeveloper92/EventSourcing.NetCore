namespace IntroductionToEventSourcing.BusinessLogic.Immutable;
using static ShoppingCartEvent;
using static ShoppingCartCommand;

public abstract record ShoppingCartCommand
{
    public record OpenShoppingCart(
        Guid ShoppingCartId,
        Guid ClientId
    ): ShoppingCartCommand;

    public record AddProductItemToShoppingCart(
        Guid ShoppingCartId,
        ProductItem ProductItem
    );

    public record RemoveProductItemFromShoppingCart(
        Guid ShoppingCartId,
        PricedProductItem ProductItem
    );

    public record ConfirmShoppingCart(
        Guid ShoppingCartId
    );

    public record CancelShoppingCart(
        Guid ShoppingCartId
    ): ShoppingCartCommand;

    private ShoppingCartCommand() {}
}

public static class ShoppingCartService
{
    public static ShoppingCartOpened Handle(OpenShoppingCart command)
    {
        return new ShoppingCartOpened(
            command.ShoppingCartId,
            command.ClientId
        );
    }

    public static ProductItemAddedToShoppingCart Handle(
        IProductPriceCalculator priceCalculator,
        AddProductItemToShoppingCart command,
        ShoppingCart shoppingCart
    )
    {
        if (command.ShoppingCartId != shoppingCart.Id)
            throw new InvalidOperationException($"The command you apply is different then shoppingCart");

        if (shoppingCart.IsClosed)
            throw new InvalidOperationException(
                $"Adding product item for cart in '{shoppingCart.Status}' status is not allowed.");

        var pricedProductItem = priceCalculator.Calculate(command.ProductItem);

        return new ProductItemAddedToShoppingCart(
            command.ShoppingCartId,
            pricedProductItem
        );
    }

    public static ProductItemRemovedFromShoppingCart Handle(
        RemoveProductItemFromShoppingCart command,
        ShoppingCart shoppingCart
    )
    {
        if (command.ShoppingCartId != shoppingCart.Id)
            throw new InvalidOperationException($"The command you apply is different then shoppingCart");

        if (shoppingCart.IsClosed)
            throw new InvalidOperationException(
                $"Removing product item for cart in '{shoppingCart.Status}' status is not allowed.");

        if (!shoppingCart.HasEnough(command.ProductItem))
            throw new InvalidOperationException("Not enough product items to remove");

        return new ProductItemRemovedFromShoppingCart(
            command.ShoppingCartId,
            command.ProductItem
        );
    }

    public static ShoppingCartConfirmed Handle(ConfirmShoppingCart command, ShoppingCart shoppingCart)
    {
        if(command.ShoppingCartId != shoppingCart.Id)
            throw new InvalidOperationException($"The command you apply is different then shoppingCart");

        if (shoppingCart.IsClosed)
            throw new InvalidOperationException($"Confirming cart in '{shoppingCart.Status}' status is not allowed.");

        if (shoppingCart.ProductItems.Length == 0)
            throw new InvalidOperationException($"Cannot confirm empty shopping cart");

        return new ShoppingCartConfirmed(
            shoppingCart.Id,
            DateTime.UtcNow
        );
    }

    public static ShoppingCartCanceled Handle(CancelShoppingCart command, ShoppingCart shoppingCart)
    {
        if (command.ShoppingCartId != shoppingCart.Id)
            throw new InvalidOperationException($"The command you apply is different then shoppingCart");

        if (shoppingCart.IsClosed)
            throw new InvalidOperationException($"Canceling cart in '{shoppingCart.Status}' status is not allowed.");

        return new ShoppingCartCanceled(
            shoppingCart.Id,
            DateTime.UtcNow
        );
    }
}
