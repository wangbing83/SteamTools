using System.Application.Columns;
using System.Application.Entities.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.IO.FileFormats;
using System.Security.Cryptography;

namespace System.Application.Entities;

/// <summary>
/// 图片表
/// </summary>
[Table("Images")]
[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Image<TUser, TUploadImageType> : IImage, INEWSEQUENTIALID
    where TUser : IUser
    where TUploadImageType : struct, Enum
{
    string DebuggerDisplay() => $"{Id}, {Type}, {SHA384}";

    [Key] // EF 主键
    public Guid Id { get; set; }

    public ImageFormat Type { get; set; }

    public long Size { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [Required] // EF not null
    [MaxLength(Hashs.String.Lengths.SHA384 + 3)]
    [MinLength(Hashs.String.Lengths.SHA384)]
    public string SHA384 { get; set; } = string.Empty;

    public bool SoftDeleted { get; set; }

    /// <summary>
    /// 上传纪录
    /// </summary>
    public virtual List<ImageUploadRecord<TUser, TUploadImageType>>? Records { get; set; }

    public string GetFileName() => SHA384 + Type.GetExtension();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreationTime { get; set; } = DateTimeOffset.Now;
}