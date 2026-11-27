using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace LifeVerse.Api.Data;

/// <summary>
/// Reading a jsonb column as text inside a query. PostgreSQL refuses string operations on jsonb ("operator does not exist: jsonb ~~ jsonb"),
/// so a LINQ filter writes JsonColumn.Text(x.SomeJson).Contains(...), which becomes a cast; the in-memory provider of the tests just reads the string.
/// A jsonb column also comes back rewritten by the database ({"a": 1}, with a space): never match "key":value as text, parse it.
/// </summary>
public static class JsonColumn
{
    public static string Text(string json) => json;

    public static void Register(ModelBuilder model) => model.HasDbFunction(typeof(JsonColumn).GetMethod(nameof(Text))!)
        .HasTranslation(arguments => new SqlUnaryExpression(ExpressionType.Convert, arguments[0], typeof(string), new StringTypeMapping("text", System.Data.DbType.String)));
}
