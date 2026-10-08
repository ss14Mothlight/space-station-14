using System.Linq;
using Content.Server.Preferences.Managers;
using Content.Shared._Starlight.Commands;
using Robust.Shared.Console;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Syntax;
using Robust.Shared.Toolshed.TypeParsers;

namespace Content.Server._Mothlight.Toolshed.Parsers;

public sealed partial class CharacterProfileCompletionParser : CustomCompletionParser<int>
{
    [Dependency] private IServerPreferencesManager _prefs = null!;

    public override CompletionResult? TryAutocomplete(ParserContext ctx, CommandArgument? arg)
    {
        if (CommandHelpers.NoSession(ctx.Session)) return null;
        var prefs = _prefs.GetPreferences(ctx.Session!.UserId).Characters
            .Select(c => new CompletionOption(c.Key.ToString(), c.Value.Name));
        return CompletionResult.FromOptions(prefs);
    }
}
