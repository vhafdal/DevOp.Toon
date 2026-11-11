namespace DevOp.Toon.Tests;

/// <summary>
/// Values and options whose encoded text is pinned by <see cref="ExtensionDataBaselineTests"/>.
/// </summary>
public static class ExtensionDataBaselineCases
{
    public static IEnumerable<(string Name, object Value, ToonEncodeOptions Options)> Legacy()
    {
        foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
        {
            var optionSets = new (string Name, ToonEncodeOptions Options)[]
            {
                ("indent2", new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 }),
                ("defaults", new ToonEncodeOptions { ProtocolVersion = version }),
                ("folding", new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, KeyFolding = ToonKeyFolding.Safe }),
                ("explicit", new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false })
            };
            foreach (var (optionName, options) in optionSets)
            {
                foreach (var (valueName, value) in LegacyValues())
                    yield return ($"{version}/{optionName}/{valueName}", value, options);
            }
        }
    }

    public static IEnumerable<(string Name, object Value, ToonEncodeOptions Options)> V4()
    {
        foreach (var version in new[]
                 {
                     ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
                     ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
                 })
        {
            var optionSets = new (string Name, ToonEncodeOptions Options)[]
            {
                ("indent2", new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 }),
                ("defaults", new ToonEncodeOptions { ProtocolVersion = version })
            };
            foreach (var (optionName, options) in optionSets)
            {
                foreach (var (valueName, value) in V4Values())
                    yield return ($"{version}/{optionName}/{valueName}", value, options);
            }
        }
    }

    private static IEnumerable<(string Name, object Value)> LegacyValues()
    {
        yield return ("root", PlainAccount("Cash", 1000));
        yield return ("list", new List<PlainAccountShape> { PlainAccount("Cash", 1000), PlainAccount("Bank", 1011) });
        yield return ("array", new[] { PlainAccount("Cash", 1000), PlainAccount("Bank", 1011) });
        yield return ("flat-list", new List<FlatShape> { new() { Name = "Cash", Number = 1 }, new() { Name = "Bank", Number = 2, Note = "n" } });
        yield return ("irregular-list", new List<PlainAccountShape> { PlainAccount("Cash", 1000), Irregular() });
        yield return ("nested", Ledger());
        yield return ("dictionary", new Dictionary<string, PlainAccountShape> { ["cash"] = PlainAccount("Cash", 1000), ["bank"] = PlainAccount("Bank", 1011) });
        yield return ("chain", new ChainShape { Id = 1, Meta = new ChainLevel { Inner = new ChainLeaf { Leaf = "x" } }, Items = [new FlatShape { Name = "a", Number = 1 }] });
        yield return ("bytes", new BytesShape { Id = 1, Data = [1, 2, 3], Items = [new BytesItem { Name = "a", Data = [1, 2, 3] }, new BytesItem { Name = "b", Data = [4] }] });
    }

    private static IEnumerable<(string Name, object Value)> V4Values()
    {
        yield return ("single", Sample());
        yield return ("uniform", Uniform());
        yield return ("irregular", IrregularAccounts());
        yield return ("ledger", new ExtensionDataTests.Ledger { Title = "Main", Primary = Sample(), Accounts = Uniform() });
        yield return ("null-entry", new ExtensionDataTests.Account { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@note"] = null, ["@empty"] = "", ["Tags"] = new List<object?>(), ["@meta"] = new Dictionary<string, object?>(), ["Kind"] = "asset" } });
        yield return ("collision", new ExtensionDataTests.AliasedAccount { Name = "Cash", Code = "A1", Annotations = new Dictionary<string, object?> { ["Code"] = "X", ["acct"] = "Y", ["Name"] = "Z", ["@id"] = "a/1" } });
        yield return ("record", new ExtensionDataTests.AccountRecord("declared", new Dictionary<string, object?> { ["name"] = "dropped", ["@id"] = "a/1" }));
        yield return ("book", new ExtensionDataTests.AccountBook
        {
            Name = "Main",
            Accounts = new ExtensionDataTests.AccountMap
            {
                Entries = new Dictionary<string, object?>
                {
                    ["cash"] = new Dictionary<string, object?> { ["Number"] = 1000, ["Title"] = "Cash" },
                    ["bank"] = new Dictionary<string, object?> { ["Number"] = 1010, ["Title"] = "Bank" }
                }
            }
        });
        yield return ("rich", new ExtensionDataTests.RichLedger
        {
            Title = "Main",
            Primary = Rich("Cash", 1000),
            Accounts = [Rich("Cash", 1000), Rich("Bank", 1011)]
        });
    }

    private static ExtensionDataTests.Account Sample() => new()
    {
        Name = "Other Expenses",
        Annotations = new Dictionary<string, object?>
        {
            ["@id"] = "accounts/6900",
            ["@metadata"] = new Dictionary<string, object?> { ["@collection"] = "Accounts" }
        }
    };

    private static List<ExtensionDataTests.Account> Uniform() =>
    [
        new() { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@id"] = "a/1" } },
        new() { Name = "Bank", Annotations = new Dictionary<string, object?> { ["@id"] = "a/2" } }
    ];

    private static List<ExtensionDataTests.Account> IrregularAccounts() =>
    [
        new() { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@id"] = "a/1", ["@flags"] = "x" } },
        new() { Name = "Bank", Annotations = new Dictionary<string, object?> { ["@id"] = "a/2" } }
    ];

    private static ExtensionDataTests.RichAccount Rich(string name, int number) => new()
    {
        Name = name, Number = number, Balance = number * 1.25m, Active = number % 2 == 0, Note = null,
        Address = new ExtensionDataTests.Address { City = "Reykjavik", Zip = "101" }, Tags = ["a", "b"],
        Annotations = new Dictionary<string, object?> { ["@id"] = $"a/{number}", ["@rev"] = number % 3 }
    };

    private static PlainAccountShape PlainAccount(string name, int number) => new()
    {
        Name = name, Number = number, Balance = number * 1.25m, Active = number % 2 == 0, Note = null,
        Address = new AddressShape { City = "Reykjavik", Zip = "101" }, Tags = ["a", "b"], Empty = []
    };

    private static PlainAccountShape Irregular() => new()
    {
        Name = "Odd", Number = 7, Balance = 1.10m, Active = true, Note = "", Address = null, Tags = [], Empty = ["e"]
    };

    private static LedgerShape Ledger() => new()
    {
        Title = "Main",
        Primary = PlainAccount("Cash", 1000),
        Accounts = [PlainAccount("Cash", 1000), PlainAccount("Bank", 1011)],
        Lookup = new Dictionary<string, AddressShape> { ["home"] = new() { City = "Akureyri", Zip = "600" } },
        Flat = [new FlatShape { Name = "a", Number = 1 }, new FlatShape { Name = "b", Number = 2 }]
    };

    public sealed class AddressShape
    {
        public string? City { get; set; }
        public string? Zip { get; set; }
    }

    public sealed class PlainAccountShape
    {
        public string? Name { get; set; }
        public int Number { get; set; }
        public decimal Balance { get; set; }
        public bool Active { get; set; }
        public string? Note { get; set; }
        public AddressShape? Address { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? Empty { get; set; }
    }

    public sealed class FlatShape
    {
        public string? Name { get; set; }
        public int Number { get; set; }
        public string? Note { get; set; }
    }

    public sealed class LedgerShape
    {
        public string? Title { get; set; }
        public PlainAccountShape? Primary { get; set; }
        public List<PlainAccountShape>? Accounts { get; set; }
        public Dictionary<string, AddressShape>? Lookup { get; set; }
        public List<FlatShape>? Flat { get; set; }
    }

    public sealed class ChainLeaf
    {
        public string? Leaf { get; set; }
    }

    public sealed class ChainLevel
    {
        public ChainLeaf? Inner { get; set; }
    }

    public sealed class ChainShape
    {
        public int Id { get; set; }
        public ChainLevel? Meta { get; set; }
        public List<FlatShape>? Items { get; set; }
    }

    public sealed class BytesItem
    {
        public string? Name { get; set; }
        public byte[]? Data { get; set; }
    }

    public sealed class BytesShape
    {
        public int Id { get; set; }
        public byte[]? Data { get; set; }
        public List<BytesItem>? Items { get; set; }
    }
}
