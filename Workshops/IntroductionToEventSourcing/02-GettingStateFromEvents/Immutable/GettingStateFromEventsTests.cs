using FluentAssertions;
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
    private ShoppingCartEvent(){}
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
);

public enum ShoppingCartStatus
{
    Pending = 1,
    Confirmed = 2,
    Canceled = 4
}

public class GettingStateFromEventsTests
{
    // 1. Add logic here
    private static ShoppingCart GetShoppingCart(IEnumerable<ShoppingCartEvent> events)
    {
        ShoppingCart shoppingCart = null!;

        foreach (var @event in events)
        {
            switch (@event)
            {
                case ShoppingCartOpened shoppingCartOpened:
                    {
                        shoppingCart = new ShoppingCart(
                            shoppingCartOpened.ShoppingCartId,
                           shoppingCartOpened.ClientId,
                           ShoppingCartStatus.Pending,
                           []
                         );
                    }
                    break;

                case ProductItemAddedToShoppingCart addedToShoppingCart:
                    {
                        if (shoppingCart.Id != addedToShoppingCart.ShoppingCartId)
                            throw new Exception("Event had a different shoppingCartId");

                        var newProductItem = new PricedProductItem(
                            addedToShoppingCart.ProductItem.ProductId,
                            addedToShoppingCart.ProductItem.Quantity,
                            addedToShoppingCart.ProductItem.UnitPrice
                        );

                        var updatedProductItems = shoppingCart.ProductItems.Select(item =>
                            item.ProductId == newProductItem.ProductId
                                ? newProductItem with { Quantity = item.Quantity + newProductItem.Quantity }
                                : item
                        ).ToList();

                        if (!updatedProductItems.Any(item => item.ProductId == newProductItem.ProductId))
                            updatedProductItems.Add(newProductItem);

                        shoppingCart = shoppingCart with
                        {
                            ProductItems = updatedProductItems.ToArray()
                        };
                    }
                    break;

                case ProductItemRemovedFromShoppingCart removedFromShoppingCart:
                    {
                        if (shoppingCart.Id != removedFromShoppingCart.ShoppingCartId)
                            throw new Exception("Event had a different shoppingCartId");

                        var updatedProductItems = shoppingCart.ProductItems
                            .Select(item =>
                                item.ProductId == removedFromShoppingCart.ProductItem.ProductId
                                    ? item with { Quantity = item.Quantity - removedFromShoppingCart.ProductItem.Quantity }
                                    : item
                            )
                            .Where(item => item.Quantity > 0) // Rimuovi elementi con quantità zero o negativa
                            .ToList();

                        shoppingCart = shoppingCart with
                        {
                            ProductItems = updatedProductItems.ToArray()
                        };
                    }
                    break;

                case ShoppingCartConfirmed shoppingCartConfirmed:
                    {
                        if (shoppingCart.Id != shoppingCartConfirmed.ShoppingCartId)
                            throw new Exception("Event had a different shoppingCartId");

                        shoppingCart = shoppingCart with
                        {
                            Status = ShoppingCartStatus.Confirmed,
                            ConfirmedAt = shoppingCartConfirmed.ConfirmedAt
                        };
                    }
                    break;

                case ShoppingCartCanceled shoppingCartCanceled:
                    {

                        if (shoppingCart.Id != shoppingCartCanceled.ShoppingCartId)
                            throw new Exception("Event had a different shoppingCartId");

                        shoppingCart = shoppingCart with
                        {
                            Status = ShoppingCartStatus.Confirmed,
                            CanceledAt = shoppingCartCanceled.CanceledAt
                        };
                    }
                    break;

                default:
                    throw new NotImplementedException($"This event {@event.GetType()} doesnt exists");

            }
        }

        return shoppingCart;
    }

    [Fact]
    [Trait("Category", "SkipCI")]
    public void GettingState_ForSequenceOfEvents_ShouldSucceed()
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

        var shoppingCart = GetShoppingCart(events);

        shoppingCart.Id.Should().Be(shoppingCartId);
        shoppingCart.ClientId.Should().Be(clientId);
        shoppingCart.ProductItems.Should().HaveCount(2);
        shoppingCart.ProductItems[0].Should().Be(pairOfShoes);
        shoppingCart.ProductItems[1].Should().Be(tShirt);
    }
}
