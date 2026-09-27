using System.Data;
using Accession.Core.Time;
using Dapper;

namespace Accession.Data;

/// <summary>Registers Dapper type handlers so timestamps and GUIDs are stored as text in the documented format.</summary>
internal static class DapperSetup
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new GuidHandler());
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = UtcTimestamp.ToText(value);
        }

        public override DateTimeOffset Parse(object value) => UtcTimestamp.Parse((string)value);
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("D");
        }

        public override Guid Parse(object value) => Guid.Parse((string)value);
    }
}
