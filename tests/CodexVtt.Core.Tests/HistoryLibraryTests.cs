using CodexVtt.Core.Errors;
using CodexVtt.Core.History;

namespace CodexVtt.Core.Tests;

public sealed class HistoryLibraryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Titles_and_collections_survive_a_new_instance()
    {
        var library = new HistoryLibrary(_dir.Path);
        library.SetTitle("a", "  Lezione  ");
        var collection = library.SaveCollection(null, "Storia", "green");
        library.SetCollection("a", collection.Id);
        library.SetCollection("b", collection.Id);

        var read = new HistoryLibrary(_dir.Path).Read();

        Assert.Equal("Lezione", read.Titles["a"]);
        Assert.Equal(["a", "b"], Assert.Single(read.Collections).EntryIds);
        Assert.Equal(collection.Id, read.CollectionOf("b"));
    }

    [Fact]
    public void An_entry_belongs_to_one_collection_at_a_time()
    {
        var library = new HistoryLibrary(_dir.Path);
        var first = library.SaveCollection(null, "Uno", "rose");
        var second = library.SaveCollection(null, "Due", "sage");

        library.SetCollection("a", first.Id);
        library.SetCollection("a", second.Id);

        Assert.Equal(second.Id, library.Read().CollectionOf("a"));
        library.SetCollection("a", null);
        Assert.Null(library.Read().CollectionOf("a"));
    }

    [Fact]
    public void Reorder_requires_the_same_entries()
    {
        var library = new HistoryLibrary(_dir.Path);
        var collection = library.SaveCollection(null, "Storia", "ochre");
        library.SetCollection("a", collection.Id);
        library.SetCollection("b", collection.Id);

        Assert.Equal(["b", "a"], library.Reorder(collection.Id, ["b", "a"]).EntryIds);
        var ex = Assert.Throws<CodexVttException>(() => library.Reorder(collection.Id, ["b", "b"]));
        Assert.Equal(ErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public void Forget_removes_title_and_membership_without_creating_the_file()
    {
        var empty = new HistoryLibrary(_dir.Path);
        empty.Forget("a");
        Assert.False(File.Exists(Path.Combine(_dir.Path, HistoryLibrary.FileName)));

        var collection = empty.SaveCollection(null, "Storia", "bordeaux");
        empty.SetTitle("a", "Lezione");
        empty.SetCollection("a", collection.Id);
        empty.Forget("a");

        var read = empty.Read();
        Assert.Empty(read.Titles);
        Assert.Empty(Assert.Single(read.Collections).EntryIds);
    }

    [Fact]
    public void Invalid_names_colors_and_ids_are_rejected()
    {
        var library = new HistoryLibrary(_dir.Path);

        Assert.Throws<CodexVttException>(() => library.SaveCollection(null, "  ", "green"));
        Assert.Throws<CodexVttException>(() => library.SaveCollection(null, "Storia", "blu"));
        Assert.Throws<CodexVttException>(() => library.DeleteCollection("col-assente"));
        Assert.Throws<CodexVttException>(() => library.SetCollection("a", "col-assente"));
    }
}
