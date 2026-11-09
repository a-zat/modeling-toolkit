namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// A tool of the 3D view: a state machine that receives the input of the pointer, with what it picks, and keys, and
/// changes the document through the commands of <see cref="IToolHost.Session"/>. It tells the user what it expects
/// next with <see cref="Hint"/>.
/// </summary>
/// <remarks>
/// A press, the drags that follow it and its release come in this order; a release without a drag in between, as the
/// view decides, is then a click, and a drag of the left button also turns the view. One tool is active at a time:
/// it receives input only between <see cref="Activate"/> and <see cref="Deactivate"/>.
/// </remarks>
public abstract class Tool
{
    private IToolHost? _host;

    /// <summary>The name of the tool, as the tool column shows it.</summary>
    public abstract string Name { get; }

    /// <summary>What the tool expects next, as the status bar shows it.</summary>
    public string Hint
    {
        get => field;
        protected set
        {
            if (field != value)
            {
                field = value;
                HintChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    } = string.Empty;

    public event EventHandler? HintChanged;

    /// <summary>
    /// The measure of what the tool is doing, as the measurement field shows it, such as the length of the line being
    /// drawn; null when the tool takes no typed value.
    /// </summary>
    public string? Measurement
    {
        get => field;
        protected set
        {
            if (field != value)
            {
                field = value;
                MeasurementChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? MeasurementChanged;

    /// <summary>What <see cref="Measurement"/> measures, such as "Length" or "Radius".</summary>
    public string MeasurementName
    {
        get => field;
        protected set
        {
            if (field != value)
            {
                field = value;
                MeasurementChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    } = "Length";

    /// <summary>A value typed in the measurement field; whether the tool took it.</summary>
    public virtual bool TypeMeasurement(string text) => false;

    public bool IsActive => _host is not null;

    /// <summary>The host of the active tool.</summary>
    /// <exception cref="InvalidOperationException">The tool is not active.</exception>
    protected IToolHost Host => _host ?? throw new InvalidOperationException($"The {Name} tool is not active.");

    /// <summary>Makes the tool receive the input, for a host.</summary>
    public void Activate(IToolHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        OnActivated();
    }

    /// <summary>Stops the tool, which drops what it was doing.</summary>
    public void Deactivate()
    {
        if (_host is not null)
        {
            OnDeactivated();
            _host = null;
        }
    }

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    /// <summary>The pointer moved, without buttons pressed.</summary>
    public virtual void Hover(ToolInput input)
    {
    }

    public virtual void Press(ToolInput input)
    {
    }

    /// <summary>The pointer moved with the left button pressed.</summary>
    public virtual void Drag(ToolInput input)
    {
    }

    public virtual void Release(ToolInput input)
    {
    }

    /// <summary>The left button was pressed and released without a drag.</summary>
    public virtual void Click(ToolInput input)
    {
    }

    /// <summary>A key was pressed; whether the tool took it, otherwise the application may.</summary>
    public virtual bool Key(ToolKey key, ToolModifiers modifiers) => false;
}
