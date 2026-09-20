using System.Collections.ObjectModel;

namespace TypeSafe.AI;

/// <summary>Entry point for fluently building the system-one questions map.</summary>
/// <example>
/// <code>
/// Questions.Build(q => q
///     .Noul("billing", "Is this ticket about billing?")
///     .Choice("tone", "What is the customer's tone?", "calm", "frustrated", "angry")
///     .Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today"))
/// </code>
/// </example>
public static class Questions
{
    /// <summary>Builds the questions map through a chained builder lambda.</summary>
    public static IReadOnlyDictionary<string, TypeSafeQuestion> Build(
        Func<QuestionBuilder, QuestionBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return new QuestionBuilder().BuildCore(build);
    }
}

/// <summary>Accumulates questions by id; every method returns the builder for chaining.</summary>
/// <remarks>Constructed only through <see cref="Questions.Build"/>. Duplicate and blank ids are
/// rejected eagerly with the id named; structural counts stay validated in <see cref="SystemOneRequest.Create"/>.</remarks>
public sealed class QuestionBuilder
{
    private readonly Dictionary<string, TypeSafeQuestion> _questions = [];

    internal QuestionBuilder()
    {
    }

    /// <summary>Adds a yes/no question. Optional outcome descriptions map to the documented true/false criteria.</summary>
    public QuestionBuilder Noul(string id, string? instructions = null,
        string? whenTrue = null, string? whenFalse = null)
    {
        NoulCriteria? criteria = whenTrue is null && whenFalse is null
            ? null
            : new NoulCriteria { WhenTrue = ToContent(whenTrue), WhenFalse = ToContent(whenFalse) };
        return Add(id, new Noul { Instructions = ToContent(instructions), Criteria = criteria });
    }

    /// <summary>Adds a yes/no question with structured instructions and optional structured criteria.</summary>
    public QuestionBuilder Noul(string id, TypeSafeContent instructions,
        TypeSafeContent? whenTrue = null, TypeSafeContent? whenFalse = null)
    {
        NoulCriteria? criteria = whenTrue is null && whenFalse is null
            ? null
            : new NoulCriteria { WhenTrue = whenTrue, WhenFalse = whenFalse };
        return Add(id, new Noul { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a yes/no question with pre-built criteria.</summary>
    public QuestionBuilder Noul(string id, TypeSafeContent? instructions, NoulCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return Add(id, new Noul { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a yes/no question without instructions and pre-built criteria.</summary>
    public QuestionBuilder Noul(string id, NoulCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return Add(id, new Noul { Instructions = null, Criteria = criteria });
    }

    /// <summary>Adds a choice question with undescribed labels.</summary>
    public QuestionBuilder Choice(string id, string instructions, params string[] labels)
        => Choice(id, ToContent(instructions), labels);

    /// <summary>Adds a choice question with structured instructions and undescribed labels.</summary>
    public QuestionBuilder Choice(string id, TypeSafeContent? instructions, params string[] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var criteria = new Dictionary<string, TypeSafeContent?>(labels.Length);
        foreach (var label in labels)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            if (!criteria.TryAdd(label, null))
                throw new ArgumentException($"An option with the label '{label}' was added more than once.", nameof(labels));
        }
        return Add(id, new Choice { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a choice question whose options are described through a sub-builder lambda.</summary>
    public QuestionBuilder Choice(string id, string instructions, Func<ChoiceOptions, ChoiceOptions> options)
        => Choice(id, ToContent(instructions), options);

    /// <summary>Adds a choice question with structured instructions whose options are described through a sub-builder lambda.</summary>
    public QuestionBuilder Choice(string id, TypeSafeContent? instructions, Func<ChoiceOptions, ChoiceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var choiceOptions = options(new ChoiceOptions())
            ?? throw new ArgumentException("The options lambda must return the options builder.", nameof(options));
        return Add(id, new Choice { Instructions = instructions, Criteria = choiceOptions.ToCriteria() });
    }

    /// <summary>Adds a choice question without instructions whose options are described through a sub-builder lambda.</summary>
    public QuestionBuilder Choice(string id, Func<ChoiceOptions, ChoiceOptions> options)
        => Choice(id, (TypeSafeContent?)null, options);

    /// <summary>Adds a rubric question with text levels.</summary>
    public QuestionBuilder Score(string id, string instructions, params string[] levels)
        => Score(id, ToContent(instructions), levels);

    /// <summary>Adds a rubric question with structured instructions and text levels.</summary>
    public QuestionBuilder Score(string id, TypeSafeContent? instructions, params string[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var criteria = new List<TypeSafeContent>(levels.Length);
        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            criteria.Add(level);
        }
        return Add(id, new Score { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a rubric question with structured instructions and structured levels.</summary>
    public QuestionBuilder Score(string id, TypeSafeContent? instructions, params TypeSafeContent[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var criteria = new List<TypeSafeContent>(levels.Length);
        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            criteria.Add(level);
        }
        return Add(id, new Score { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a rubric question without instructions and structured levels.</summary>
    public QuestionBuilder Score(string id, params TypeSafeContent[] levels)
        => Score(id, (TypeSafeContent?)null, levels);

    /// <summary>Adds a rubric question whose levels are built through a sub-builder lambda.</summary>
    public QuestionBuilder Score(string id, string instructions, Func<ScoreLevels, ScoreLevels> levels)
        => Score(id, ToContent(instructions), levels);

    /// <summary>Adds a rubric question with structured instructions whose levels are built through a sub-builder lambda.</summary>
    public QuestionBuilder Score(string id, TypeSafeContent? instructions, Func<ScoreLevels, ScoreLevels> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var scoreLevels = levels(new ScoreLevels())
            ?? throw new ArgumentException("The levels lambda must return the levels builder.", nameof(levels));
        return Add(id, new Score { Instructions = instructions, Criteria = scoreLevels.ToCriteria() });
    }

    /// <summary>Adds a rubric question without instructions whose levels are built through a sub-builder lambda.</summary>
    public QuestionBuilder Score(string id, Func<ScoreLevels, ScoreLevels> levels)
        => Score(id, (TypeSafeContent?)null, levels);

    private QuestionBuilder Add(string id, TypeSafeQuestion question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_questions.TryAdd(id, question))
            throw new ArgumentException($"A question with the id '{id}' has already been added.", nameof(id));
        return this;
    }

    private static TypeSafeContent? ToContent(string? text) => text is null ? null : TypeSafeContent.FromString(text);

    internal IReadOnlyDictionary<string, TypeSafeQuestion> BuildCore(Func<QuestionBuilder, QuestionBuilder> build)
    {
        if (build(this) is null)
            throw new ArgumentException("The build lambda must return the builder.", nameof(build));
        if (_questions.Count == 0)
            throw new ArgumentException("At least one question is required.", nameof(build));
        return new ReadOnlyDictionary<string, TypeSafeQuestion>(_questions);
    }
}

/// <summary>Accumulates choice options; undescribed options map to the documented null criteria values.</summary>
public sealed class ChoiceOptions
{
    private readonly Dictionary<string, TypeSafeContent?> _options = [];

    internal ChoiceOptions()
    {
    }

    /// <summary>Adds an option; a null description leaves the label undescribed.</summary>
    public ChoiceOptions Option(string label, TypeSafeContent? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!_options.TryAdd(label, description))
            throw new ArgumentException($"An option with the label '{label}' was added more than once.", nameof(label));
        return this;
    }

    internal IReadOnlyDictionary<string, TypeSafeContent?> ToCriteria()
        => new ReadOnlyDictionary<string, TypeSafeContent?>(_options);
}

/// <summary>Accumulates ordered score rubric levels.</summary>
public sealed class ScoreLevels
{
    private readonly List<TypeSafeContent> _levels = [];

    internal ScoreLevels()
    {
    }

    /// <summary>Adds one rubric level; plain strings convert implicitly.</summary>
    public ScoreLevels Level(TypeSafeContent level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _levels.Add(level);
        return this;
    }

    internal IReadOnlyList<TypeSafeContent> ToCriteria() => _levels.AsReadOnly();
}
