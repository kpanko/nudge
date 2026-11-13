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
    public void Item_CanBelongToAProject()
    {
        var house = new Project("House");
        var item = new Item("Measure the worktop") { Project = house };

        Assert.Same(house, item.Project);
    }
}
