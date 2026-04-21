using System;
using System.Collections.Generic;
using System.Text.Json;
using DevOp.Toon;
using Xunit;

namespace DevOp.Toon.Tests.Encode;

[Trait("Category", "encode")]
public class ArraysObjectsManual
{
    [Fact]
    [Trait("Description", "tabular array with multiple sibling fields")]
    public void TabularArrayWithMultipleSiblingFields()
    {
        // Arrange
        var input =
            new
            {
                @items = new object[] {
                    new
                    {
                        @users = new object[] {
                            new { @id = 1, @name = "Ada" },
                            new { @id = 2, @name = "Bob" },
                        },
                        @status = "active",
                        @count = 2,
                        @tags = new object[] { "a", "b", "c" }
                    }
                }
            };

        var expected =
"""
items[1]{status,count}:
  active,2
    users[2]{id,name}:
      1,Ada
      2,Bob
    tags[3]: a,b,c
""";

        // Act & Assert
        var result = ToonEncoder.Encode(input, new LegacyDefaultToonEncodeOptions());

        Assert.Equal(expected, result);
    }

    [Fact]
    [Trait("Description", "multiple list items with tabular first fields")]
    public void MultipleListItemsWithTabularFirstFields()
    {
        // Arrange
        var input =
            new
            {
                @items = new object[] {
                    new
                    {
                        @users = new object[] {
                            new { @id = 1, @name = "Ada" },
                            new { @id = 2, @name = "Bob" },
                        },
                        @status = "active"
                    },
                    new
                    {
                        @users = new object[] {
                            new { @id = 3, @name = "Charlie" }
                        },
                        @status = "inactive"
                    }
                }
            };

        var expected =
"""
items[2]{status}:
  active
    users[2]{id,name}:
      1,Ada
      2,Bob
  inactive
    users[1]{id,name}:
      3,Charlie
""";

        // Act & Assert
        var result = ToonEncoder.Encode(input, new LegacyDefaultToonEncodeOptions());

        Assert.Equal(expected, result);
    }

    [Fact]
    [Trait("Description", "deeply nested list items with tabular first field")]
    public void DeeplyNestedListItemsWithTabularFirstField()
    {
        // Arrange
        var input =
            new
            {
                @data = new object[] {
                    new
                    {
                        @items = new object[] {
                            new
                            {
                                @users = new object[] {
                                    new { @id = 1, @name = "Ada" },
                                    new { @id = 2, @name = "Bob" },
                                },
                                @status = "active"
                            }
                        }
                    }
                }
            };

        var expected =
"""
data[1]:
  - items[1]{status}:
      active
        users[2]{id,name}:
          1,Ada
          2,Bob
""";

        // Act & Assert
        var result = ToonEncoder.Encode(input, new LegacyDefaultToonEncodeOptions());

        Assert.Equal(expected, result);
    }
}
