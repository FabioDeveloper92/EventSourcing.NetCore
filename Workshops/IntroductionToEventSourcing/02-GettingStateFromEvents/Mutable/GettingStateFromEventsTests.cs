using FluentAssertions;
using Xunit;

namespace IntroductionToEventSourcing.GettingStateFromEvents.Mutable;
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
public class PricedProductItem
{
    public Guid ProductId { get; set; }
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal TotalPrice => Quantity * UnitPrice;
}

// ENTITY
public class ShoppingCart
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public ShoppingCartStatus Status { get; set; }
    public IList<PricedProductItem> ProductItems { get; set; } = new List<PricedProductItem>();
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CanceledAt { get; set; }

    public void Evolve(object @event)
    {
        switch (@event)
        {
            case ShoppingCartOpened shoppingCartOpened:
                Apply(shoppingCartOpened);
                break;
            case ProductItemAddedToShoppingCart productItemAdded:
                Apply(productItemAdded);
                break;
            case ProductItemRemovedFromShoppingCart productItemRemoved:
                Apply(productItemRemoved);
                break;
            case ShoppingCartConfirmed shoppingCartConfirmed:
                Apply(shoppingCartConfirmed);
                break;
            case ShoppingCartCanceled shoppingCartCanceled:
                Apply(shoppingCartCanceled);
                break;
        }
    }

    private void Apply(ShoppingCartOpened shoppingCartOpened)
    {
        Id = shoppingCartOpened.ShoppingCartId;
        ClientId = shoppingCartOpened.ClientId;
        Status = ShoppingCartStatus.Pending;
    }

    private void Apply(ProductItemAddedToShoppingCart productItemAdded)
    {
        if (productItemAdded.ShoppingCartId != Id)
            throw new Exception("You are trying to apply an event to a different ShoppingCart");

        var current = ProductItems.SingleOrDefault(
            pi => pi.ProductId == productItemAdded.ProductItem.ProductId
        );

        if (current == null)
            ProductItems.Add(productItemAdded.ProductItem);
        else
            current.Quantity += productItemAdded.ProductItem.Quantity;
    }

    private void Apply(ProductItemRemovedFromShoppingCart productItemRemoved)
    {
        if (productItemRemoved.ShoppingCartId != Id)
            throw new Exception("You are trying to apply an event to a different ShoppingCart");

        var current = ProductItems.SingleOrDefault(
            pi => pi.ProductId == productItemRemoved.ProductItem.ProductId
        );

        if (current is null)
            throw new Exception("Product not found in the cart");

        if (current.Quantity >= productItemRemoved.ProductItem.Quantity)
            ProductItems.Remove(current);
        else
            current.Quantity -= productItemRemoved.ProductItem.Quantity;
    }

    private void Apply(ShoppingCartConfirmed shoppingCartConfirmed)
    {
        if (shoppingCartConfirmed.ShoppingCartId != Id)
            throw new Exception("You are trying to apply an event to a different ShoppingCart");

        Status = ShoppingCartStatus.Confirmed;
        ConfirmedAt = shoppingCartConfirmed.ConfirmedAt;
    }

    private void Apply(ShoppingCartCanceled shoppingCartCanceled)
    {
        if (shoppingCartCanceled.ShoppingCartId != Id)
            throw new Exception("You are trying to apply an event to a different ShoppingCart");

        Status = ShoppingCartStatus.Canceled;
        CanceledAt = shoppingCartCanceled.CanceledAt;
    }

}

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
        var shoppingCart = new ShoppingCart();

        foreach (var @event in events)
        {
            shoppingCart.Evolve(@event);
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
        var twoPairsOfShoes =
            new PricedProductItem
            {
                ProductId = shoesId,
                Quantity = 2,
                UnitPrice = 100
            };
        var pairOfShoes =
            new PricedProductItem
            {
                ProductId = shoesId,
                Quantity = 1,
                UnitPrice = 100
            };
        var tShirt =
            new PricedProductItem
            {
                ProductId = tShirtId,
                Quantity = 1,
                UnitPrice = 50
            };

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

        shoppingCart.ProductItems[0].ProductId.Should().Be(shoesId);
        shoppingCart.ProductItems[0].Quantity.Should().Be(pairOfShoes.Quantity);
        shoppingCart.ProductItems[0].UnitPrice.Should().Be(pairOfShoes.UnitPrice);

        shoppingCart.ProductItems[1].ProductId.Should().Be(tShirtId);
        shoppingCart.ProductItems[1].Quantity.Should().Be(tShirt.Quantity);
        shoppingCart.ProductItems[1].UnitPrice.Should().Be(tShirt.UnitPrice);
    }
}
