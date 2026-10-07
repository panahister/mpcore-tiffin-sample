using MPCore.Domain.Model;

namespace Tiffin.Restaurants.Domain;

/// <summary>One thing a restaurant sells. It has an identity inside its restaurant, the code, and none outside.</summary>
public sealed class MenuItem : Entity<string>
{
    private MenuItem()
    {
        Name = string.Empty;
    }

    internal MenuItem(string code, string name, decimal price, bool available, Guid? pictureId = null)
        : base(code)
    {
        Name = name;
        Price = price;
        IsAvailable = available;
        PictureId = pictureId;
    }

    public string Code => Id;

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Sold out for today, without leaving the menu.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>The food picture kept by Media. The menu stores only its identifier.</summary>
    public Guid? PictureId { get; private set; }

    internal void Change(string name, decimal price, bool available, Guid? pictureId = null)
    {
        Name = name;
        Price = price;
        IsAvailable = available;
        // SetMenuItem is a full update for authored menu fields, but an omitted optional picture must
        // not erase a previously uploaded image. Media deletion remains an explicit Media operation.
        if (pictureId.HasValue)
        {
            PictureId = pictureId;
        }
    }
}
