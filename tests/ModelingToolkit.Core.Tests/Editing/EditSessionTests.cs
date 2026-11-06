using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class EditSessionTests
{
    private static MeshEntity Triangle(string? name = null) =>
        new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new Triangle(0, 1, 2)]) { Name = name };

    [Fact]
    public void Commands_are_undone_and_redone_in_order()
    {
        var document = new Document();
        MeshEntity mesh = Triangle("Floor");
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);
        var oak = new Material("Oak");

        session.Execute(new RenameCommand(mesh, "Deck"));
        session.Execute(new SetMaterialCommand(mesh, oak));
        Assert.Equal("Change Material", session.UndoName);

        session.Undo();
        Assert.Null(mesh.Material);
        Assert.Equal("Deck", mesh.Name);
        Assert.Equal("Change Material", session.RedoName);

        session.Undo();
        Assert.Equal("Floor", mesh.Name);
        Assert.False(session.CanUndo);

        session.Redo();
        session.Redo();
        Assert.Equal("Deck", mesh.Name);
        Assert.Same(oak, mesh.Material);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void A_new_command_forgets_what_could_be_redone()
    {
        var document = new Document();
        MeshEntity mesh = Triangle();
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);

        session.Execute(new RenameCommand(mesh, "A"));
        session.Undo();
        session.Execute(new RenameCommand(mesh, "B"));

        Assert.False(session.CanRedo);
        Assert.Null(session.RedoName);
        session.Undo();
        Assert.Null(mesh.Name);
    }

    [Fact]
    public void A_deleted_entity_comes_back_where_it_was()
    {
        var document = new Document();
        MeshEntity first = Triangle("First"), second = Triangle("Second"), third = Triangle("Third");
        document.Root.Entities.Add(first);
        document.Root.Entities.Add(second);
        document.Root.Entities.Add(third);
        var session = new EditSession(document);

        session.Execute(new DeleteCommand(second));
        Assert.Equal([first, third], document.Root.Entities);
        Assert.Null(second.Owner);

        session.Undo();
        Assert.Equal([first, second, third], document.Root.Entities);

        session.Redo();
        Assert.Equal([first, third], document.Root.Entities);
        Assert.Throws<ArgumentException>(() => new DeleteCommand(second));
    }

    [Fact]
    public void A_committed_transaction_undoes_as_one_step()
    {
        var document = new Document();
        MeshEntity mesh = Triangle("Start");
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);

        using (EditSession.Transaction transaction = session.Begin("Drag"))
        {
            session.Execute(new RenameCommand(mesh, "One"));
            session.Execute(new RenameCommand(mesh, "Two"));
            Assert.False(session.CanUndo); // not while the gesture goes on
            transaction.Commit();
        }

        Assert.Equal("Drag", session.UndoName);
        session.Undo();
        Assert.Equal("Start", mesh.Name);
        session.Redo();
        Assert.Equal("Two", mesh.Name);
    }

    [Fact]
    public void A_transaction_disposed_without_commit_reverts_its_commands()
    {
        var document = new Document();
        MeshEntity mesh = Triangle("Start");
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);

        using (session.Begin("Drag"))
        {
            session.Execute(new RenameCommand(mesh, "One"));
            session.Execute(new DeleteCommand(mesh));
        }

        Assert.Equal("Start", mesh.Name);
        Assert.Same(document.Root, mesh.Owner);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void An_empty_transaction_leaves_nothing_to_undo()
    {
        var session = new EditSession(new Document());

        session.Begin("Nothing").Commit();

        Assert.False(session.CanUndo);
        Assert.Throws<InvalidOperationException>(session.Undo);
        Assert.Throws<InvalidOperationException>(session.Redo);
    }

    [Fact]
    public void Transactions_do_not_nest()
    {
        var session = new EditSession(new Document());
        using EditSession.Transaction outer = session.Begin("Outer");

        Assert.Throws<InvalidOperationException>(() => session.Begin("Inner"));
    }

    [Fact]
    public void A_refused_command_changes_nothing_and_is_not_kept()
    {
        var document = new Document();
        MeshEntity mesh = Triangle("Start");
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);
        var composite = new CompositeCommand("Both", [new RenameCommand(mesh, "Changed"), new RefusingCommand()]);

        Assert.Throws<EditRefusedException>(() => session.Execute(composite));

        Assert.Equal("Start", mesh.Name);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void The_session_knows_whether_the_document_changed_since_it_was_saved()
    {
        var document = new Document();
        MeshEntity mesh = Triangle();
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);
        int notified = 0;
        session.Changed += (_, _) => notified++;
        Assert.False(session.IsModified);

        session.Execute(new RenameCommand(mesh, "A"));
        Assert.True(session.IsModified);
        session.MarkSaved();
        Assert.False(session.IsModified);
        session.Undo();
        Assert.True(session.IsModified);
        session.Redo();
        Assert.False(session.IsModified);
        Assert.Equal(4, notified);
    }

    [Fact]
    public void A_command_cannot_run_another_one_while_it_changes_the_document()
    {
        var document = new Document();
        MeshEntity mesh = Triangle();
        document.Root.Entities.Add(mesh);
        var session = new EditSession(document);
        document.Changed += (_, _) => session.Execute(new RenameCommand(mesh, "Nested"));

        Assert.Throws<InvalidOperationException>(() => session.Execute(new RenameCommand(mesh, "A")));
    }

    private sealed class RefusingCommand : IEditCommand
    {
        public string Name => "Refuse";

        public void Apply() => throw new EditRefusedException("No.");

        public void Revert() => throw new InvalidOperationException("Never applied.");
    }
}
