using Microsoft.EntityFrameworkCore;
using Nudge.Core;

namespace Nudge.Web.Tests;

public sealed class ProjectPersistenceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    private void Save(params Project[] projects)
    {
        using var db = _database.NewContext();
        db.Projects.AddRange(projects);
        db.SaveChanges();
    }

    [Fact]
    public void Projects_KeepTheirNesting()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var tiling = new Project("Tiling", kitchen);

        // Saving the innermost project saves the ones above it.
        Save(tiling);

        using var db = _database.NewContext();
        var projects = db.Projects.ToList();
        Assert.Equal(3, projects.Count);
        var loaded = projects.Single(p => p.Id == tiling.Id);
        Assert.Equal(
            ["Kitchen", "House"],
            loaded.Ancestors().Select(p => p.Name));
        Assert.Null(projects.Single(p => p.Id == house.Id).Parent);
    }

    [Fact]
    public void LoadedProjects_StillRefuseALoop()
    {
        var house = new Project("House");
        var kitchen = new Project("Kitchen", house);
        var tiling = new Project("Tiling", kitchen);
        Save(tiling);

        using var db = _database.NewContext();
        var projects = db.Projects.ToList();
        var loadedHouse = projects.Single(p => p.Id == house.Id);
        var loadedTiling = projects.Single(p => p.Id == tiling.Id);

        Assert.Throws<InvalidOperationException>(
            () => loadedHouse.MoveUnder(loadedTiling));
    }

    [Fact]
    public void MoveUnder_IsSaved()
    {
        var house = new Project("House");
        var garden = new Project("Garden");
        var shed = new Project("Shed", house);
        Save(shed, garden);

        using (var db = _database.NewContext())
        {
            var projects = db.Projects.ToList();
            projects.Single(p => p.Id == shed.Id)
                .MoveUnder(projects.Single(p => p.Id == garden.Id));
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            var projects = db.Projects.ToList();
            var loaded = projects.Single(p => p.Id == shed.Id);
            Assert.Equal("Garden", loaded.Parent?.Name);

            loaded.MoveUnder(null);
            loaded.Name = "Bike shed";
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            var loaded = db.Projects.ToList().Single(p => p.Id == shed.Id);
            Assert.Null(loaded.Parent);
            Assert.Equal("Bike shed", loaded.Name);
        }
    }

    [Fact]
    public void Item_CanMoveBetweenProjects()
    {
        var house = new Project("House");
        var garden = new Project("Garden");
        Save(house, garden);
        var item = new Item("Fix the gate");
        using (var db = _database.NewContext())
        {
            item.Project = db.Projects.Single(p => p.Id == house.Id);
            db.Items.Add(item);
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            var loaded = db.Items.Single(i => i.Id == item.Id);
            Assert.Equal("House", loaded.Project?.Name);
            loaded.Project = db.Projects.Single(p => p.Id == garden.Id);
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            var loaded = db.Items.Single(i => i.Id == item.Id);
            Assert.Equal("Garden", loaded.Project?.Name);
            loaded.Project = null;
            db.SaveChanges();
        }

        using (var db = _database.NewContext())
        {
            Assert.Null(db.Items.Single(i => i.Id == item.Id).Project);
        }

        Assert.Equal(2L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
    }

    // Ids come from the entities, so a project this context has
    // not read looks new to it. That must fail, not make a copy.
    [Fact]
    public void ProjectFromAnotherContext_IsRefused_NotCopied()
    {
        var house = new Project("House");
        Save(house);

        using var db = _database.NewContext();
        db.Items.Add(new Item("Fix the gate") { Project = house });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Items"));
    }

    [Fact]
    public void Project_WithProjectsUnderIt_CannotBeDeleted()
    {
        var house = new Project("House");
        Save(new Project("Kitchen", house));

        using var db = _database.NewContext();
        db.Projects.Remove(db.Projects.Single(p => p.Id == house.Id));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Equal(2L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
    }

    [Fact]
    public void Project_WithItems_CannotBeDeleted()
    {
        var house = new Project("House");
        using (var db = _database.NewContext())
        {
            db.Items.Add(new Item("Measure the worktop") { Project = house });
            db.SaveChanges();
        }

        using var delete = _database.NewContext();
        delete.Projects.Remove(
            delete.Projects.Single(p => p.Id == house.Id));

        Assert.Throws<DbUpdateException>(() => delete.SaveChanges());
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
        Assert.Equal(1L, _database.Scalar("SELECT COUNT(*) FROM Items"));
    }

    [Fact]
    public void EmptyProject_CanBeDeleted()
    {
        var house = new Project("House");
        Save(house);

        using var db = _database.NewContext();
        db.Projects.Remove(db.Projects.Single(p => p.Id == house.Id));
        db.SaveChanges();

        Assert.Equal(0L, _database.Scalar("SELECT COUNT(*) FROM Projects"));
    }
}
