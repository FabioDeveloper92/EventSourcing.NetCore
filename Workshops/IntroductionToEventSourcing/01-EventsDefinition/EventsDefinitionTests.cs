using FluentAssertions;
using Xunit;

namespace IntroductionToEventSourcing.EventsDefinition;

// 1. Define your events and entity here

public record ShoppingCartOpened(Guid cartId, Guid userId);
public record ProductAddedToShoppingCart(Guid cartId, Product product);
public record ProductRemovedFromShoppingCart(Guid cartId, Product product);
public record ShoppingCartConfirmed(Guid cartId, DateTime confirmedAt);
public record ShoppingCartCanceled(Guid cartId, DateTime canceledAt);

// Object
public class Product(Guid id, decimal sellPrice, int quantity)
{
    public Guid Id { get; private set; } = id;
    public decimal SellPrice { get; private set; } = sellPrice;
    public int Quantity { get; private set; } = quantity;
    public decimal TotalPrice => Quantity * SellPrice;

    public static Product Create(Guid id, decimal sellPrice, int quantity)
    {
        if (quantity <= 0)
            throw new Exception("Invalid quantity");

        var item = new Product(id, sellPrice, quantity);
        return item;
    }
}

public class EventsDefinitionTests
{
    [Fact]
    [Trait("Category", "SkipCI")]
    public void AllEventTypes_ShouldBeDefined()
    {
        var cartId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var product = Product.Create(Guid.NewGuid(), 10m, 2);

        var events = new object[]
        {
           new ShoppingCartOpened(cartId, userId),
           new ProductAddedToShoppingCart(cartId,product),
           new ProductRemovedFromShoppingCart(cartId,product),
           new ShoppingCartConfirmed(cartId, DateTime.Now),
           new ShoppingCartCanceled(cartId, DateTime.Now)
        };

        const int expectedEventTypesCount = 5;
        events.Should().HaveCount(expectedEventTypesCount);
        events.GroupBy(e => e.GetType()).Should().HaveCount(expectedEventTypesCount);
    }
}
