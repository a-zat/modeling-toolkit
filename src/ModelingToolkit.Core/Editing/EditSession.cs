using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// Runs the commands that change a document, and keeps them to undo and redo them. The commands of a transaction,
/// such as those of a gesture of a tool, undo and redo as one step.
/// </summary>
public sealed class EditSession
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private Transaction? _transaction;
    private bool _running;

    public EditSession(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
    }

    public Document Document { get; }

    public bool CanUndo => _undo.Count > 0 && _transaction is null;

    public bool CanRedo => _redo.Count > 0 && _transaction is null;

    /// <summary>The name of the command that <see cref="Undo"/> reverts; null when there is none.</summary>
    public string? UndoName => CanUndo ? _undo.Peek().Name : null;

    /// <summary>The name of the command that <see cref="Redo"/> applies again; null when there is none.</summary>
    public string? RedoName => CanRedo ? _redo.Peek().Name : null;

    /// <summary>Whether the document changed since it was opened or since <see cref="MarkSaved"/>.</summary>
    public bool IsModified => !ReferenceEquals(_undo.TryPeek(out IEditCommand? top) ? top : null, _saved);

    private IEditCommand? _saved;

    /// <summary>Raised when what can be undone or redone changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Applies a command. Outside a transaction it becomes the step to undo, and what could be redone is forgotten.
    /// </summary>
    /// <exception cref="EditRefusedException">The command refused its change; nothing was changed.</exception>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Run(command.Apply);
        if (_transaction is { } transaction)
        {
            transaction.Commands.Add(command);
            return;
        }

        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts a transaction: the commands executed until it is committed undo as one step, named
    /// <paramref name="name"/>. Disposed without being committed, it reverts them.
    /// </summary>
    /// <exception cref="InvalidOperationException">A transaction is already open.</exception>
    public Transaction Begin(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open.");
        }

        _transaction = new Transaction(this, name);
        Changed?.Invoke(this, EventArgs.Empty);
        return _transaction;
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("There is nothing to undo.");
        }

        IEditCommand command = _undo.Peek();
        Run(command.Revert);
        _redo.Push(_undo.Pop());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <exception cref="EditRefusedException">The command refused its change this time; it can still be redone.</exception>
    public void Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("There is nothing to redo.");
        }

        IEditCommand command = _redo.Peek();
        Run(command.Apply);
        _undo.Push(_redo.Pop());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers the current step as the one saved: <see cref="IsModified"/> is false until the next change.</summary>
    public void MarkSaved()
    {
        _saved = _undo.TryPeek(out IEditCommand? top) ? top : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A command must not run another one through the session, from a handler of the changes of the document.
    private void Run(Action action)
    {
        if (_running)
        {
            throw new InvalidOperationException("A command is already running.");
        }

        _running = true;
        try
        {
            action();
        }
        finally
        {
            _running = false;
        }
    }

    private void Close(Transaction transaction, bool commit)
    {
        _transaction = null;
        if (commit && transaction.Commands.Count > 0)
        {
            _undo.Push(new CompositeCommand(transaction.Name, transaction.Commands));
            _redo.Clear();
        }
        else if (!commit)
        {
            Run(() => new CompositeCommand(transaction.Name, transaction.Commands).Revert());
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The commands of a gesture, which undo as one step once committed; disposing it without committing reverts them.
    /// </summary>
    public sealed class Transaction : IDisposable
    {
        private readonly EditSession _session;
        private bool _closed;

        internal Transaction(EditSession session, string name)
        {
            _session = session;
            Name = name;
        }

        public string Name { get; }

        internal List<IEditCommand> Commands { get; } = [];

        /// <summary>Keeps the commands as one step to undo; with none, nothing is kept.</summary>
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _closed = true;
            _session.Close(this, commit: true);
        }

        /// <summary>Reverts the commands, unless the transaction was committed.</summary>
        public void Dispose()
        {
            if (!_closed)
            {
                _closed = true;
                _session.Close(this, commit: false);
            }
        }
    }
}
