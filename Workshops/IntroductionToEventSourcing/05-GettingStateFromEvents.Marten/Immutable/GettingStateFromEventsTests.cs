using FluentAssertions;
using IntroductionToEventSourcing.GettingStateFromEvents.Tools;
using Marten;
using Xunit;

namespace IntroductionToEventSourcing.GettingStateFromEvents.Immutable;
using static ShoppingCartEvent;

// EVENTS
public abstract record ShoppingCartEvent
{
    public record ShoppingCartOpened(
        Guid ShoppingCartId,
        Guid ClientId
    ): ShoppingCartEvent;

    public record ProductItemAddedToShoppingCart(
        Guid ShoppingCartId,
        PricedProductItem ProductItem
    ): ShoppingCartEvent;

    public record ProductItemRemovedFromShoppingCart(
        Guid ShoppingCartId,
        PricedProductItem ProductItem
    ): ShoppingCartEvent;

    public record ShoppingCartConfirmed(
        Guid ShoppingCartId,
        DateTime ConfirmedAt
    ): ShoppingCartEvent;

    public record ShoppingCartCanceled(
        Guid ShoppingCartId,
        DateTime CanceledAt
    ): ShoppingCartEvent;

    // This won't allow external inheritance
    private ShoppingCartEvent() { }
}

// VALUE OBJECTS
public record PricedProductItem(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice
);

// ENTITY
public record ShoppingCart(
    Guid Id,
    Guid ClientId,
    ShoppingCartStatus Status,
    PricedProductItem[] ProductItems,
    DateTime? ConfirmedAt = null,
    DateTime? CanceledAt = null
)
{
    public ShoppingCart Apply(ShoppingCartEvent @event) =>
        @event switch
        {
            ShoppingCartOpened shoppingCartOpened => new ShoppingCart(
                shoppingCartOpened.ShoppingCartId,
                shoppingCartOpened.ClientId,
                ShoppingCartStatus.Pending,
                Array.Empty<PricedProductItem>()
            ),

            ProductItemAddedToShoppingCart addedToShoppingCart => ApplyProductItemAdded(addedToShoppingCart),

            ProductItemRemovedFromShoppingCart removedFromShoppingCart => ApplyProductItemRemoved(removedFromShoppingCart),

            ShoppingCartConfirmed shoppingCartConfirmed => ApplyConfirmed(shoppingCartConfirmed),

            ShoppingCartCanceled shoppingCartCanceled => ApplyCanceled(shoppingCartCanceled),

            _ => throw new NotImplementedException($"Event of type {@event.GetType()} is not supported.")
        };

    private ShoppingCart ApplyProductItemAdded(ProductItemAddedToShoppingCart @event)
    {
        if (Id != @event.ShoppingCartId)
            throw new InvalidOperationException("Event has a different ShoppingCartId.");

        var newProductItem = new PricedProductItem(
            @event.ProductItem.ProductId,
            @event.ProductItem.Quantity,
            @event.ProductItem.UnitPrice
        );

        var updatedProductItems = ProductItems
            .Select(item =>
                item.ProductId == newProductItem.ProductId
                    ? item with { Quantity = item.Quantity + newProductItem.Quantity }
                    : item
            )
            .ToList();

        if (!updatedProductItems.Any(item => item.ProductId == newProductItem.ProductId))
            updatedProductItems.Add(newProductItem);

        return this with { ProductItems = updatedProductItems.ToArray() };
    }

    private ShoppingCart ApplyProductItemRemoved(ProductItemRemovedFromShoppingCart @event)
    {
        if (Id != @event.ShoppingCartId)
            throw new InvalidOperationException("Event has a different ShoppingCartId.");

        var updatedProductItems = ProductItems
            .Select(item =>
                item.ProductId == @event.ProductItem.ProductId
                    ? item with { Quantity = item.Quantity - @event.ProductItem.Quantity }
                    : item
            )
            .Where(item => item.Quantity > 0)
            .ToArray();

        return this with { ProductItems = updatedProductItems };
    }

    private ShoppingCart ApplyConfirmed(ShoppingCartConfirmed @event)
    {
        if (Id != @event.ShoppingCartId)
            throw new InvalidOperationException("Event has a different ShoppingCartId.");

        return this with
        {
            Status = ShoppingCartStatus.Confirmed,
            ConfirmedAt = @event.ConfirmedAt
        };
    }

    private ShoppingCart ApplyCanceled(ShoppingCartCanceled @event)
    {
        if (Id != @event.ShoppingCartId)
            throw new InvalidOperationException("Event has a different ShoppingCartId.");

        return this with
        {
            Status = ShoppingCartStatus.Canceled,
            CanceledAt = @event.CanceledAt
        };
    }

    private ShoppingCart() : this(Guid.Empty, Guid.Empty, ShoppingCartStatus.Pending, Array.Empty<PricedProductItem>()) { }
}


public enum ShoppingCartStatus
{
    Pending = 1,
    Confirmed = 2,
    Canceled = 4
}

public class GettingStateFromEventsTests: MartenTest
{
    /// <summary>
    /// Solution - Immutable entity
    /// </summary>
    /// <param name="documentSession"></param>
    /// <param name="shoppingCartId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task<ShoppingCart> GetShoppingCart(IDocumentSession documentSession, Guid shoppingCartId,
       CancellationToken cancellationToken)
    {
        var shoppingCart = await documentSession.Events.AggregateStreamAsync<ShoppingCart>(shoppingCartId, token: cancellationToken);

        return shoppingCart ?? throw new InvalidOperationException("Shopping Cart doesnt exist!");
    }

    [Fact]
    [Trait("Category", "SkipCI")]
    public async Task GettingState_FromMarten_ShouldSucceed()
    {
        var shoppingCartId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var shoesId = Guid.NewGuid();
        var tShirtId = Guid.NewGuid();
        var twoPairsOfShoes = new PricedProductItem(shoesId, 2, 100);
        var pairOfShoes = new PricedProductItem(shoesId, 1, 100);
        var tShirt = new PricedProductItem(tShirtId, 1, 50);

        var events = new object[]
        {
            new ShoppingCartOpened(shoppingCartId, clientId),
            new ProductItemAddedToShoppingCart(shoppingCartId, twoPairsOfShoes),
            new ProductItemAddedToShoppingCart(shoppingCartId, tShirt),
            new ProductItemRemovedFromShoppingCart(shoppingCartId, pairOfShoes),
            new ShoppingCartConfirmed(shoppingCartId, DateTime.UtcNow),
            new ShoppingCartCanceled(shoppingCartId, DateTime.UtcNow)
        };

        await AppendEvents(shoppingCartId, events, CancellationToken.None);

        var shoppingCart = await GetShoppingCart(DocumentSession, shoppingCartId, CancellationToken.None);

        shoppingCart.Id.Should().Be(shoppingCartId);
        shoppingCart.ClientId.Should().Be(clientId);
        shoppingCart.ProductItems.Should().HaveCount(2);

        shoppingCart.ProductItems[0].ProductId.Should().Be(shoesId);
        shoppingCart.ProductItems[0].Quantity.Should().Be(pairOfShoes.Quantity);
        shoppingCart.ProductItems[0].UnitPrice.Should().Be(pairOfShoes.UnitPrice);

        shoppingCart.ProductItems[1].ProductId.Should().Be(tShirtId);
        shoppingCart.ProductItems[1].Quantity.Should().Be(tShirt.Quantity);
        shoppingCart.ProductItems[1].UnitPrice.Should().Be(tShirt.UnitPrice);
    }
}
