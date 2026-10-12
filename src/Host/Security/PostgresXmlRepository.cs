using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace FamilyApp.Host.Security;

/// <summary>
/// Stores ASP.NET Core Data Protection keys in Postgres (ADR-023).
/// The interface is synchronous by design; key reads are rare and cached by the framework.
/// </summary>
public sealed class PostgresXmlRepository(NpgsqlDataSource dataSource) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var command = dataSource.CreateCommand(
            "SELECT xml FROM platform.data_protection_keys ORDER BY created_at");
        using var reader = command.ExecuteReader();

        var elements = new List<XElement>();
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var command = dataSource.CreateCommand(
            "INSERT INTO platform.data_protection_keys (id, friendly_name, xml) VALUES ($1, $2, $3)");
        command.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
        command.Parameters.Add(new NpgsqlParameter { Value = friendlyName });
        command.Parameters.Add(new NpgsqlParameter { Value = element.ToString(SaveOptions.DisableFormatting) });
        command.ExecuteNonQuery();
    }
}
