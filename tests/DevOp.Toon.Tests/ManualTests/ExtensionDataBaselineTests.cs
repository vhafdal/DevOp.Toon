using System.Text;

namespace DevOp.Toon.Tests;

/// <summary>
/// Pins encoder output captured before legacy extension-data support: types without the attribute on
/// the legacy profiles, and attributed types on the 4.x profiles, must stay byte-identical.
/// </summary>
public sealed class ExtensionDataBaselineTests
{
    public static TheoryData<string> LegacyCases => Names(ExtensionDataBaselineCases.Legacy());

    public static TheoryData<string> V4Cases => Names(ExtensionDataBaselineCases.V4());

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void Encode_LegacyTypeWithoutAttribute_ByteIdenticalToBaseline(string name)
    {
        var (_, value, options) = ExtensionDataBaselineCases.Legacy().Single(c => c.Name == name);

        AssertAllPaths(baselines[name], value, options);
    }

    [Theory]
    [MemberData(nameof(V4Cases))]
    public void Encode_V4ExtensionData_ByteIdenticalToBaseline(string name)
    {
        var (_, value, options) = ExtensionDataBaselineCases.V4().Single(c => c.Name == name);

        AssertAllPaths(baselines[name], value, options);
    }

    private static void AssertAllPaths(string expected, object value, ToonEncodeOptions options)
    {
        using var stream = new MemoryStream();
        ToonEncoder.EncodeToStream(value, stream, options);

        Assert.Equal(expected, ToonEncoder.Encode(value, options));
        Assert.Equal(expected, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(value, options)));
        Assert.Equal(expected, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static TheoryData<string> Names(IEnumerable<(string Name, object Value, ToonEncodeOptions Options)> cases)
    {
        var data = new TheoryData<string>();
        foreach (var entry in cases) data.Add(entry.Name);
        return data;
    }

    private static readonly Dictionary<string, string> baselines = new()
    {
        ["V3_0/indent2/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nNote: null\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b\nEmpty[0]:",
        ["V3_0/indent2/list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/indent2/array"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/indent2/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["V3_0/indent2/irregular-list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Odd\n    Number: 7\n    Balance: 1.10\n    Active: true\n    Note: \"\"\n    Address: null\n    Tags[0]:\n    Empty[1]: e",
        ["V3_0/indent2/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\nLookup:\n  home:\n    City: Akureyri\n    Zip: \"600\"\nFlat[2]{Name,Number,Note}:\n  a,1,null\n  b,2,null",
        ["V3_0/indent2/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["V3_0/indent2/chain"] = "Id: 1\nMeta:\n  Inner:\n    Leaf: x\nItems[1]{Name,Number,Note}:\n  a,1,null",
        ["V3_0/indent2/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["V3_0/defaults/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nNote: null\nAddress:\n City: Reykjavik\n Zip: \"101\"\nTags[2]: a,b\nEmpty[0]:",
        ["V3_0/defaults/list"] = "[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["V3_0/defaults/array"] = "[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["V3_0/defaults/flat-list"] = "[2]{Name,Number,Note}:\n Cash,1,null\n Bank,2,n",
        ["V3_0/defaults/irregular-list"] = "[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\n - Name: Odd\n  Number: 7\n  Balance: 1.10\n  Active: true\n  Note: \"\"\n  Address: null\n  Tags[0]:\n  Empty[1]: e",
        ["V3_0/defaults/nested"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250.00\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n Empty[0]:\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nLookup:\n home:\n  City: Akureyri\n  Zip: \"600\"\nFlat[2]{Name,Number,Note}:\n a,1,null\n b,2,null",
        ["V3_0/defaults/dictionary"] = "cash:\n Name: Cash\n Number: 1000\n Balance: 1250.00\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n Empty[0]:\nbank:\n Name: Bank\n Number: 1011\n Balance: 1263.75\n Active: false\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n Empty[0]:",
        ["V3_0/defaults/chain"] = "Id: 1\nMeta:\n Inner:\n  Leaf: x\nItems[1]{Name,Number,Note}:\n a,1,null",
        ["V3_0/defaults/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n a,AQID\n b,BA==",
        ["V3_0/folding/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nNote: null\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b\nEmpty[0]:",
        ["V3_0/folding/list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/folding/array"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/folding/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["V3_0/folding/irregular-list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Odd\n    Number: 7\n    Balance: 1.10\n    Active: true\n    Note: \"\"\n    Address: null\n    Tags[0]:\n    Empty[1]: e",
        ["V3_0/folding/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\nLookup.home:\n  City: Akureyri\n  Zip: \"600\"\nFlat[2]{Name,Number,Note}:\n  a,1,null\n  b,2,null",
        ["V3_0/folding/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["V3_0/folding/chain"] = "Id: 1\nMeta.Inner.Leaf: x\nItems[1]{Name,Number,Note}:\n  a,1,null",
        ["V3_0/folding/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["V3_0/explicit/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nNote: null\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b\nEmpty[0]:",
        ["V3_0/explicit/list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/explicit/array"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["V3_0/explicit/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["V3_0/explicit/irregular-list"] = "[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Odd\n    Number: 7\n    Balance: 1.10\n    Active: true\n    Note: \"\"\n    Address: null\n    Tags[0]:\n    Empty[1]: e",
        ["V3_0/explicit/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250.00\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\nLookup:\n  home:\n    City: Akureyri\n    Zip: \"600\"\nFlat[2]{Name,Number,Note}:\n  a,1,null\n  b,2,null",
        ["V3_0/explicit/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["V3_0/explicit/chain"] = "Id: 1\nMeta:\n  Inner:\n    Leaf: x\nItems[1]{Name,Number,Note}:\n  a,1,null",
        ["V3_0/explicit/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["CustomLegacy/indent2/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b",
        ["CustomLegacy/indent2/list"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b",
        ["CustomLegacy/indent2/array"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b",
        ["CustomLegacy/indent2/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["CustomLegacy/indent2/irregular-list"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Odd,7,1.10,true\n    Empty[1]: e",
        ["CustomLegacy/indent2/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\nAccounts[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\nLookup:\n  home:\n    City: Akureyri\n    Zip: \"600\"\nFlat[2]{Name,Number}:\n  a,1\n  b,2",
        ["CustomLegacy/indent2/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b",
        ["CustomLegacy/indent2/chain"] = "Id: 1\nMeta:\n  Inner:\n    Leaf: x\nItems[1]{Name,Number}:\n  a,1",
        ["CustomLegacy/indent2/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["CustomLegacy/defaults/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nAddress:\n City: Reykjavik\n Zip: \"101\"\nTags[2]: a,b",
        ["CustomLegacy/defaults/list"] = "[2]{Name,Number,Balance,Active}:\n Cash,1000,1250.00,true\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n Bank,1011,1263.75,false\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b",
        ["CustomLegacy/defaults/array"] = "[2]{Name,Number,Balance,Active}:\n Cash,1000,1250.00,true\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n Bank,1011,1263.75,false\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b",
        ["CustomLegacy/defaults/flat-list"] = "[2]{Name,Number,Note}:\n Cash,1,null\n Bank,2,n",
        ["CustomLegacy/defaults/irregular-list"] = "[2]{Name,Number,Balance,Active}:\n Cash,1000,1250.00,true\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n Odd,7,1.10,true\n  Empty[1]: e",
        ["CustomLegacy/defaults/nested"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250.00\n Active: true\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\nAccounts[2]{Name,Number,Balance,Active}:\n Cash,1000,1250.00,true\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n Bank,1011,1263.75,false\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\nLookup:\n home:\n  City: Akureyri\n  Zip: \"600\"\nFlat[2]{Name,Number}:\n a,1\n b,2",
        ["CustomLegacy/defaults/dictionary"] = "cash:\n Name: Cash\n Number: 1000\n Balance: 1250.00\n Active: true\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\nbank:\n Name: Bank\n Number: 1011\n Balance: 1263.75\n Active: false\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b",
        ["CustomLegacy/defaults/chain"] = "Id: 1\nMeta:\n Inner:\n  Leaf: x\nItems[1]{Name,Number}:\n a,1",
        ["CustomLegacy/defaults/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n a,AQID\n b,BA==",
        ["CustomLegacy/folding/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b",
        ["CustomLegacy/folding/list"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b",
        ["CustomLegacy/folding/array"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b",
        ["CustomLegacy/folding/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["CustomLegacy/folding/irregular-list"] = "[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Odd,7,1.10,true\n    Empty[1]: e",
        ["CustomLegacy/folding/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\nAccounts[2]{Name,Number,Balance,Active}:\n  Cash,1000,1250.00,true\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n  Bank,1011,1263.75,false\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\nLookup.home:\n  City: Akureyri\n  Zip: \"600\"\nFlat[2]{Name,Number}:\n  a,1\n  b,2",
        ["CustomLegacy/folding/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b",
        ["CustomLegacy/folding/chain"] = "Id: 1\nMeta.Inner.Leaf: x\nItems[1]{Name,Number}:\n  a,1",
        ["CustomLegacy/folding/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["CustomLegacy/explicit/root"] = "Name: Cash\nNumber: 1000\nBalance: 1250.00\nActive: true\nNote: null\nAddress:\n  City: Reykjavik\n  Zip: \"101\"\nTags[2]: a,b\nEmpty[0]:",
        ["CustomLegacy/explicit/list"] = "[2]{Name,Number,Balance,Active,Note}:\n  Cash,1000,1250.00,true,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  Bank,1011,1263.75,false,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["CustomLegacy/explicit/array"] = "[2]{Name,Number,Balance,Active,Note}:\n  Cash,1000,1250.00,true,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  Bank,1011,1263.75,false,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:",
        ["CustomLegacy/explicit/flat-list"] = "[2]{Name,Number,Note}:\n  Cash,1,null\n  Bank,2,n",
        ["CustomLegacy/explicit/irregular-list"] = "[2]{Name,Number,Balance,Active,Note}:\n  Cash,1000,1250.00,true,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  Odd,7,1.10,true,\"\"\n    Address: null\n    Tags[0]:\n    Empty[1]: e",
        ["CustomLegacy/explicit/nested"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nAccounts[2]{Name,Number,Balance,Active,Note}:\n  Cash,1000,1250.00,true,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\n  Bank,1011,1263.75,false,null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    Empty[0]:\nLookup:\n  home:\n    City: Akureyri\n    Zip: \"600\"\nFlat[2]{Name,Number,Note}:\n  a,1,null\n  b,2,null",
        ["CustomLegacy/explicit/dictionary"] = "cash:\n  Name: Cash\n  Number: 1000\n  Balance: 1250.00\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:\nbank:\n  Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  Empty[0]:",
        ["CustomLegacy/explicit/chain"] = "Id: 1\nMeta:\n  Inner:\n    Leaf: x\nItems[1]{Name,Number,Note}:\n  a,1,null",
        ["CustomLegacy/explicit/bytes"] = "Id: 1\nData: AQID\nItems[2]{Name,Data}:\n  a,AQID\n  b,BA==",
        ["V4_0/indent2/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts",
        ["V4_0/indent2/uniform"] = "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_0/indent2/irregular"] = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2",
        ["V4_0/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_0/indent2/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_0/indent2/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_0/indent2/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_0/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["V4_0/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1000\n    \"@rev\": 1\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1011\n    \"@rev\": 0",
        ["V4_0/defaults/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n \"@collection\": Accounts",
        ["V4_0/defaults/uniform"] = "[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_0/defaults/irregular"] = "[2]:\n - Name: Cash\n  \"@id\": a/1\n  \"@flags\": x\n - Name: Bank\n  \"@id\": a/2",
        ["V4_0/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n \"@id\": accounts/6900\n \"@metadata\":\n  \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_0/defaults/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_0/defaults/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_0/defaults/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_0/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["V4_0/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n \"@id\": a/1000\n \"@rev\": 1\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1011\n  \"@rev\": 0",
        ["V4_1/indent2/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts",
        ["V4_1/indent2/uniform"] = "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_1/indent2/irregular"] = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2",
        ["V4_1/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_1/indent2/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_1/indent2/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_1/indent2/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_1/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["V4_1/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1000\n    \"@rev\": 1\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1011\n    \"@rev\": 0",
        ["V4_1/defaults/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n \"@collection\": Accounts",
        ["V4_1/defaults/uniform"] = "[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_1/defaults/irregular"] = "[2]:\n - Name: Cash\n  \"@id\": a/1\n  \"@flags\": x\n - Name: Bank\n  \"@id\": a/2",
        ["V4_1/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n \"@id\": accounts/6900\n \"@metadata\":\n  \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_1/defaults/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_1/defaults/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_1/defaults/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_1/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["V4_1/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n \"@id\": a/1000\n \"@rev\": 1\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1011\n  \"@rev\": 0",
        ["V4_2/indent2/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts",
        ["V4_2/indent2/uniform"] = "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_2/indent2/irregular"] = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2",
        ["V4_2/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_2/indent2/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_2/indent2/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_2/indent2/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_2/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["V4_2/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1000\n    \"@rev\": 1\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1011\n    \"@rev\": 0",
        ["V4_2/defaults/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n \"@collection\": Accounts",
        ["V4_2/defaults/uniform"] = "[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_2/defaults/irregular"] = "[2]:\n - Name: Cash\n  \"@id\": a/1\n  \"@flags\": x\n - Name: Bank\n  \"@id\": a/2",
        ["V4_2/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n \"@id\": accounts/6900\n \"@metadata\":\n  \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_2/defaults/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_2/defaults/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_2/defaults/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_2/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["V4_2/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n \"@id\": a/1000\n \"@rev\": 1\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1011\n  \"@rev\": 0",
        ["V4_3/indent2/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts",
        ["V4_3/indent2/uniform"] = "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_3/indent2/irregular"] = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2",
        ["V4_3/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_3/indent2/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_3/indent2/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_3/indent2/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_3/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["V4_3/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1000\n    \"@rev\": 1\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1011\n    \"@rev\": 0",
        ["V4_3/defaults/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n \"@collection\": Accounts",
        ["V4_3/defaults/uniform"] = "[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_3/defaults/irregular"] = "[2]:\n - Name: Cash\n  \"@id\": a/1\n  \"@flags\": x\n - Name: Bank\n  \"@id\": a/2",
        ["V4_3/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n \"@id\": accounts/6900\n \"@metadata\":\n  \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_3/defaults/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_3/defaults/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_3/defaults/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_3/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["V4_3/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n \"@id\": a/1000\n \"@rev\": 1\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1011\n  \"@rev\": 0",
        ["V4_4/indent2/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts",
        ["V4_4/indent2/uniform"] = "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_4/indent2/irregular"] = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2",
        ["V4_4/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2",
        ["V4_4/indent2/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_4/indent2/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_4/indent2/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_4/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["V4_4/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\nAccounts[2]:\n  - Name: Cash\n    Number: 1000\n    Balance: 1250\n    Active: true\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1000\n    \"@rev\": 1\n  - Name: Bank\n    Number: 1011\n    Balance: 1263.75\n    Active: false\n    Note: null\n    Address:\n      City: Reykjavik\n      Zip: \"101\"\n    Tags[2]: a,b\n    \"@id\": a/1011\n    \"@rev\": 0",
        ["V4_4/defaults/single"] = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n \"@collection\": Accounts",
        ["V4_4/defaults/uniform"] = "[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_4/defaults/irregular"] = "[2]:\n - Name: Cash\n  \"@id\": a/1\n  \"@flags\": x\n - Name: Bank\n  \"@id\": a/2",
        ["V4_4/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n \"@id\": accounts/6900\n \"@metadata\":\n  \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n Cash,a/1\n Bank,a/2",
        ["V4_4/defaults/null-entry"] = "Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags: []\n\"@meta\":\nKind: asset",
        ["V4_4/defaults/collision"] = "Name: Cash\nacct: A1\n\"@id\": a/1",
        ["V4_4/defaults/record"] = "Name: declared\n\"@id\": a/1",
        ["V4_4/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["V4_4/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n \"@id\": a/1000\n \"@rev\": 1\nAccounts[2]:\n - Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1000\n  \"@rev\": 1\n - Name: Bank\n  Number: 1011\n  Balance: 1263.75\n  Active: false\n  Note: null\n  Address:\n   City: Reykjavik\n   Zip: \"101\"\n  Tags[2]: a,b\n  \"@id\": a/1011\n  \"@rev\": 0",
        ["CustomV4_4/indent2/single"] = "Name: Other Expenses\n@id: accounts/6900\n@metadata:\n  @collection: Accounts",
        ["CustomV4_4/indent2/uniform"] = "[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2",
        ["CustomV4_4/indent2/irregular"] = "[2]{Name,@id}:\n  Cash,a/1\n    @flags: x\n  Bank,a/2",
        ["CustomV4_4/indent2/ledger"] = "Title: Main\nPrimary:\n  Name: Other Expenses\n  @id: accounts/6900\n  @metadata:\n    @collection: Accounts\nAccounts[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2",
        ["CustomV4_4/indent2/null-entry"] = "Name: Cash\n@note: null\n@empty: \"\"\n@meta:\nKind: asset",
        ["CustomV4_4/indent2/collision"] = "Name: Cash\nacct: A1\n@id: a/1",
        ["CustomV4_4/indent2/record"] = "Name: declared\n@id: a/1",
        ["CustomV4_4/indent2/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n  cash: 1000,Cash\n  bank: 1010,Bank",
        ["CustomV4_4/indent2/rich"] = "Title: Main\nPrimary:\n  Name: Cash\n  Number: 1000\n  Balance: 1250\n  Active: true\n  Note: null\n  Address:\n    City: Reykjavik\n    Zip: \"101\"\n  Tags[2]: a,b\n  @id: a/1000\n  @rev: 1\nAccounts[2]{Name,Number,Balance,Active,Address{City,Zip},@id,@rev}:\n  Cash,1000,1250,true,Reykjavik,\"101\",a/1000,1\n    Tags[2]: a,b\n  Bank,1011,1263.75,false,Reykjavik,\"101\",a/1011,0\n    Tags[2]: a,b",
        ["CustomV4_4/defaults/single"] = "Name: Other Expenses\n@id: accounts/6900\n@metadata:\n @collection: Accounts",
        ["CustomV4_4/defaults/uniform"] = "[2]{Name,@id}:\n Cash,a/1\n Bank,a/2",
        ["CustomV4_4/defaults/irregular"] = "[2]{Name,@id}:\n Cash,a/1\n  @flags: x\n Bank,a/2",
        ["CustomV4_4/defaults/ledger"] = "Title: Main\nPrimary:\n Name: Other Expenses\n @id: accounts/6900\n @metadata:\n  @collection: Accounts\nAccounts[2]{Name,@id}:\n Cash,a/1\n Bank,a/2",
        ["CustomV4_4/defaults/null-entry"] = "Name: Cash\n@note: null\n@empty: \"\"\n@meta:\nKind: asset",
        ["CustomV4_4/defaults/collision"] = "Name: Cash\nacct: A1\n@id: a/1",
        ["CustomV4_4/defaults/record"] = "Name: declared\n@id: a/1",
        ["CustomV4_4/defaults/book"] = "Name: Main\nAccounts[2:]{Number,Title}:\n cash: 1000,Cash\n bank: 1010,Bank",
        ["CustomV4_4/defaults/rich"] = "Title: Main\nPrimary:\n Name: Cash\n Number: 1000\n Balance: 1250\n Active: true\n Note: null\n Address:\n  City: Reykjavik\n  Zip: \"101\"\n Tags[2]: a,b\n @id: a/1000\n @rev: 1\nAccounts[2]{Name,Number,Balance,Active,Address{City,Zip},@id,@rev}:\n Cash,1000,1250,true,Reykjavik,\"101\",a/1000,1\n  Tags[2]: a,b\n Bank,1011,1263.75,false,Reykjavik,\"101\",a/1011,0\n  Tags[2]: a,b",
    };
}
