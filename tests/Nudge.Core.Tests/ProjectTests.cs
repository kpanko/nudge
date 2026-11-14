namespace Nudge.Core.Tests;

public class ProjectTests
{
    [Fact]
    public void Projects_Nest()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var tiling = new Project(" Tiling ", kitchen);

        Assert.Null(house.Parent);
        Assert.Same(kitchen, tiling.Parent);
        Assert.Equal([kitchen, house], tiling.Ancestors());
        Assert.Equal("Tiling", tiling.Name);
    }

    [Fact]
    public void MoveUnder_ChangesParent()
    {
        var house = new Project("House");
        var garden = new Project("Garden");
        var shed = new Project("Shed", house);

        shed.MoveUnder(garden);
        Assert.Same(garden, shed.Parent);

        shed.MoveUnder(null);
        Assert.Null(shed.Parent);
    }

    [Fact]
    public void MoveUnder_ItselfOrADescendant_IsRefused()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var tiling = new Project("Tiling", kitchen);

        Assert.Throws<InvalidOperationException>(
            () => house.MoveUnder(house));
        Assert.Throws<InvalidOperationException>(
            () => house.MoveUnder(tiling));
        Assert.Null(house.Parent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankNames_AreRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => new Project(name));
        Assert.Throws<ArgumentException>(() => new Context(name));
    }

    [Fact]
    public void Delete_MarksTheProjectOrContext_Once()
    {
        var at = new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var phone = new Context("phone");
        Assert.False(house.IsDeleted);
        Assert.False(phone.IsDeleted);

        house.Delete(at);
        phone.Delete(at);

        Assert.Equal(at, house.DeletedAt);
        Assert.True(house.IsDeleted);
        Assert.Equal(at, phone.DeletedAt);
        Assert.True(phone.IsDeleted);

        // A project does not know what is under it, so only the
        // project itself is marked.
        Assert.False(kitchen.IsDeleted);
        Assert.Same(house, kitchen.Parent);

        Assert.Throws<InvalidOperationException>(
            () => house.Delete(at.AddHours(1)));
        Assert.Throws<InvalidOperationException>(
            () => phone.Delete(at.AddHours(1)));
        Assert.Equal(at, house.DeletedAt);
        Assert.Equal(at, phone.DeletedAt);
    }

    [Fact]
    public void Item_CanBelongToAProject()
    {
        var house = new Project("House");
        var item = new Item("Measure the worktop") { Project = house };

        Assert.Same(house, item.Project);
    }
}
