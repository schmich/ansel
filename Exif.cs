using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleTableExt;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

static class Exif
{
    public static string ToTable(ExifProfile profile)
    {
        var data = new List<List<object>>();
        foreach (var exif in profile.Values.OrderBy(e => e.Tag.ToString()))
        {
            var value = exif.GetValue() ?? "(null)";
            var isArray = exif.GetValue()?.GetType().IsArray ?? false;

            if (value is Array arr)
            {
                var values = new List<string>();
                for (var i = 0; i < arr.Length; i++)
                {
                    values.Add(arr.GetValue(i)?.ToString() ?? "(null)");
                }

                value = $"[{string.Join(", ", values)}]";
            }

            var type = $"{exif.DataType}{(isArray ? "[]" : "")}";

            var svalue = value?.ToString() ?? "(null)";
            svalue = svalue.Length > 100 ? svalue[..97] + "..." : svalue;

            data.Add([exif.Tag, type, exif.GetValue()?.GetType().Name ?? "(null)", svalue]);
        }

        var table = ConsoleTableBuilder
            .From(data)
            .WithColumn("Tag", "Exif Type", "GetType", "Value")
            .WithCharMapDefinition(CharMapDefinition.FramePipDefinition)
            .Export()
            .ToString()
            .Trim();

        return table;
    }

    public static ExifProfile? FilterProfile(ExifProfile? profile, int maxEntrySizeBytes)
    {
        if (profile == null)
        {
            return null;
        }

        profile = profile.DeepClone();

        var removeTags = new List<ExifTag>();
        foreach (var record in profile.Values)
        {
            var size = GetExifRecordSizeBytes(record);
            if (size == null || size > maxEntrySizeBytes)
            {
                removeTags.Add(record.Tag);
            }
        }

        foreach (var removeTag in removeTags)
        {
            profile.RemoveValue(removeTag);
        }

        return profile;
    }

    static int? GetExifRecordSizeBytes(IExifValue? record)
    {
        if (record == null)
        {
            return 0;
        }

        var value = record.GetValue();
        if (value == null)
        {
            return 0;
        }

        if (value is Array a)
        {
            int total = 0;
            for (var i = 0; i < a.Length; i++)
            {
                var size = GetExifValueSize(record.DataType, a.GetValue(i));
                if (size == null)
                {
                    return null;
                }

                total += size.Value;
            }

            return total;
        }

        return GetExifValueSize(record.DataType, value);
    }

    static int? GetExifValueSize(ExifDataType type, object? value)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is string s)
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        return type switch
        {
            ExifDataType.Undefined => 1,
            ExifDataType.Byte => 1,
            ExifDataType.SignedByte => 1,
            ExifDataType.Short => 2,
            ExifDataType.SignedShort => 2,
            ExifDataType.Ifd => 4,
            ExifDataType.Long => 4,
            ExifDataType.SignedLong => 4,
            ExifDataType.SingleFloat => 4,
            ExifDataType.Ifd8 => 8,
            ExifDataType.Long8 => 8,
            ExifDataType.SignedLong8 => 8,
            ExifDataType.Rational => 8,
            ExifDataType.SignedRational => 8,
            ExifDataType.DoubleFloat => 8,
            _ => null
        };
    }

    public static string? GetComment(ExifProfile? profile)
    {
        if (profile == null)
        {
            return null;
        }

        return GetString(profile, ExifTag.XPComment)
            ?? GetString(profile, ExifTag.ImageDescription)
            ?? GetString(profile, ExifTag.UserComment);
    }

    public static DateTimeOffset? GetTakenAt(ExifProfile? profile)
    {
        if (profile == null)
        {
            return null;
        }

        return GetDateTime(profile, ExifTag.DateTime, ExifTag.OffsetTime)
            ?? GetDateTime(profile, ExifTag.DateTimeOriginal, ExifTag.OffsetTimeOriginal)
            ?? GetDateTime(profile, ExifTag.DateTimeDigitized, ExifTag.OffsetTimeDigitized)
            ?? GetDate(profile, ExifTag.GPSDateStamp);
    }

    static string? GetString(ExifProfile profile, ExifTag<string> tag)
    {
        var value = profile.TryGetValue(tag, out var result)
            ? result.Value?.Trim()?.Trim('\0')
            : null;

        return string.IsNullOrEmpty(value) ? null : value;
    }

    static string? GetString(ExifProfile profile, ExifTag<EncodedString> tag)
    {
        var value = profile.TryGetValue(tag, out var result)
            ? result?.Value.Text?.Trim()?.Trim('\0')
            : null;

        return string.IsNullOrEmpty(value) ? null : value;
    }

    static DateTimeOffset? GetDateTime(ExifProfile profile, ExifTag<string> dateTimeTag, ExifTag<string> offsetTag)
    {
        var dateTime = GetString(profile, dateTimeTag);
        var offset = GetString(profile, offsetTag);

        if (string.IsNullOrWhiteSpace(dateTime))
        {
            return null;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;

        if (!string.IsNullOrWhiteSpace(offset) && DateTimeOffset.TryParseExact($"{dateTime}{offset}", "yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture, styles, out var dateTimeOffset))
        {
            return dateTimeOffset;
        }
        else if (DateTimeOffset.TryParseExact(dateTime, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, styles, out var dateTimeOnly))
        {
            return dateTimeOnly;
        }

        return null;
    }

    static DateTimeOffset? GetDate(ExifProfile profile, ExifTag<string> dateTag)
    {
        var date = GetString(profile, dateTag);
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(date, "yyyy:MM:dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)
            ? dateOnly
            : null;
    }
}

class ExifProfileConverter : JsonConverter<ExifProfile>
{
    public override ExifProfile? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("expected string value for ExifProfile");
        }

        var base64 = reader.GetString()
            ?? throw new JsonException("expected string value for ExifProfile");

        var exifBytes = Convert.FromBase64String(base64);
        return new ExifProfile(exifBytes);
    }

    public override void Write(Utf8JsonWriter writer, ExifProfile? value, JsonSerializerOptions options)
    {
        var bytes = value?.ToByteArray();
        if (bytes == null)
        {
            writer.WriteNullValue();
            return;
        }

        var base64 = Convert.ToBase64String(bytes);
        writer.WriteStringValue(base64);
    }
}