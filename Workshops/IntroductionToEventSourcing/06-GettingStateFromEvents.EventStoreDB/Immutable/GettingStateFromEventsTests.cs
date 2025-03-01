using EventStore.Client;
using FluentAssertions;
using IntroductionToEventSourcing.GettingStateFromEvents.Tools;
using System.Text.Json;
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
    public static ShoppingCart Default() => new(default, default, default, []);

    public static ShoppingCart Evolve(ShoppingCart shoppingCart, object @event) =>
        @event switch
        {
            ShoppingCartOpened evt => HandleShoppingCartOpened(shoppingCart, evt),
            ProductItemAddedToShoppingCart evt => HandleProductItemAdded(shoppingCart, evt),
            ProductItemRemovedFromShoppingCart evt => HandleProductItemRemoved(shoppingCart, evt),
            ShoppingCartConfirmed evt => HandleShoppingCartConfirmed(shoppingCart, evt),
            ShoppingCartCanceled evt => HandleShoppingCartCanceled(shoppingCart, evt),
            _ => shoppingCart
        };

    private static ShoppingCart HandleShoppingCartOpened(ShoppingCart shoppingCart, ShoppingCartOpened evt) =>
        shoppingCart with
        {
            Id = evt.ShoppingCartId,
            ClientId = evt.ClientId,
            Status = ShoppingCartStatus.Pending
        };

    private static ShoppingCart HandleProductItemAdded(ShoppingCart shoppingCart, ProductItemAddedToShoppingCart evt)
    {
        var updatedProductItems = shoppingCart.ProductItems
            .Concat(new[] { evt.ProductItem })
            .GroupBy(pi => pi.ProductId)
            .Select(group => group.Count() == 1
                ? group.First()
                : new PricedProductItem(
                    group.Key,
                    group.Sum(pi => pi.Quantity),
                    group.First().UnitPrice
                ))
            .ToArray();

        return shoppingCart with { ProductItems = updatedProductItems };
    }

    private static ShoppingCart HandleProductItemRemoved(ShoppingCart shoppingCart, ProductItemRemovedFromShoppingCart evt)
    {
        var updatedProductItems = shoppingCart.ProductItems
            .Select(pi => pi.ProductId == evt.ProductItem.ProductId
                ? pi with { Quantity = pi.Quantity - evt.ProductItem.Quantity }
                : pi)
            .Where(pi => pi.Quantity > 0)
            .ToArray();

        return shoppingCart with { ProductItems = updatedProductItems };
    }

    private static ShoppingCart HandleShoppingCartConfirmed(ShoppingCart shoppingCart, ShoppingCartConfirmed evt) =>
        shoppingCart with
        {
            Status = ShoppingCartStatus.Confirmed,
            ConfirmedAt = evt.ConfirmedAt
        };

    private static ShoppingCart HandleShoppingCartCanceled(ShoppingCart shoppingCart, ShoppingCartCanceled evt) =>
        shoppingCart with
        {
            Status = ShoppingCartStatus.Canceled,
            CanceledAt = evt.CanceledAt
        };
}

public enum ShoppingCartStatus
{
    Pending = 1,
    Confirmed = 2,
    Canceled = 4
}

public class GettingStateFromEventsTests: EventStoreDBTest
{
    /// <summary>
    /// Solution - Mutable entity with When method
    /// </summary>
    /// <returns></returns>
    private static async Task<ShoppingCart> GetShoppingCart(EventStoreClient eventStore, string streamName, CancellationToken ct)
    {
        var res = eventStore.ReadStreamAsync(
            Direction.Forwards,
            streamName,
            StreamPosition.Start,
            cancellationToken: ct
        );

        if (await res.ReadState == ReadState.StreamNotFound)
            throw new InvalidOperationException("Shopping Cart doesnt exist!");

        return await res
            .Select(@event =>
                JsonSerializer.Deserialize(
                    @event.Event.Data.Span,
                    Type.GetType(@event.Event.EventType, true)!
                )!
            )
            .AggregateAsync(
                ShoppingCart.Default(),
                ShoppingCart.Evolve,
                ct
            );
    }

    [Fact]
    [Trait("Category", "SkipCI")]
    public async Task GettingState_FromEventStoreDB_ShouldSucceed()
    {
        var shoppingCartId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var shoesId = Guid.NewGuid();
        var tShirtId = Guid.NewGuid();
        var twoPairsOfShoes = new PricedProductItem(shoesId, 2, 100);
        var pairOfShoes = new PricedProductItem(shoesId, 1, 100);
        var tShirt = new PricedProductItem(tShirtId, 1, 50);

        var events = new ShoppingCartEvent[]
        {
            new ShoppingCartOpened(shoppingCartId, clientId),
            new ProductItemAddedToShoppingCart(shoppingCartId, twoPairsOfShoes),
            new ProductItemAddedToShoppingCart(shoppingCartId, tShirt),
            new ProductItemRemovedFromShoppingCart(shoppingCartId, pairOfShoes),
            new ShoppingCartConfirmed(shoppingCartId, DateTime.UtcNow),
            new ShoppingCartCanceled(shoppingCartId, DateTime.UtcNow)
        };

        var streamName = $"shopping_cart-{shoppingCartId}";

        await AppendEvents(streamName, events, CancellationToken.None);

        var shoppingCart = await GetShoppingCart(EventStore, streamName, CancellationToken.None);

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
