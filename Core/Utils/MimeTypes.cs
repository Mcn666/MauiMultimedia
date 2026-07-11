using System.Collections.Generic;
using System.IO;

namespace MauiMultimedia.Core.Utils;

/// <summary>
/// 扩展名 → MIME 类型的完整标准映射表。
/// 作为本地 HTTP 文件服务（<see cref="MauiMultimedia.Core.Abstractions.IFileServerService"/>）设置
/// <c>Content-Type</c> 的默认来源，避免由宿主（Shell）手维护一份易过期的局部表。
/// 数据源自 IANA / Apache <c>mime.types</c> 标准类型。
/// 未知扩展名回退到 <see cref="Fallback"/>（application/octet-stream）。
/// </summary>
public static class MimeTypes
{
    /// <summary>未知扩展名的兜底 MIME 类型（触发浏览器下载而非内联渲染）。</summary>
    public const string Fallback = "application/octet-stream";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Images ──
        { "jpg", "image/jpeg" },
        { "jpeg", "image/jpeg" },
        { "jpe", "image/jpeg" },
        { "png", "image/png" },
        { "apng", "image/apng" },
        { "gif", "image/gif" },
        { "webp", "image/webp" },
        { "bmp", "image/bmp" },
        { "dib", "image/bmp" },
        { "ico", "image/x-icon" },
        { "cur", "image/x-icon" },
        { "avif", "image/avif" },
        { "svg", "image/svg+xml" },
        { "svgz", "image/svg+xml" },
        { "dds", "image/x-dds" },
        { "tiff", "image/tiff" },
        { "tif", "image/tiff" },
        { "heic", "image/heic" },
        { "heif", "image/heif" },
        { "jxl", "image/jxl" },
        { "jp2", "image/jp2" },
        { "jpf", "image/jpx" },
        { "psd", "image/vnd.adobe.photoshop" },
        { "ai", "application/postscript" },
        { "eps", "application/postscript" },
        { "cr2", "image/x-canon-cr2" },
        { "cr3", "image/x-canon-cr3" },
        { "nef", "image/x-nikon-nef" },
        { "arw", "image/x-sony-arw" },
        { "raw", "image/x-panasonic-raw" },
        { "raf", "image/x-fuji-raf" },
        { "pcx", "image/x-pcx" },
        { "tga", "image/x-tga" },
        { "xcf", "image/x-xcf" },
        { "xwd", "image/x-xwindowdump" },

        // ── Audio ──
        { "mp3", "audio/mpeg" },
        { "mp2", "audio/mpeg" },
        { "wav", "audio/wav" },
        { "wave", "audio/wav" },
        { "ogg", "audio/ogg" },
        { "oga", "audio/ogg" },
        { "opus", "audio/ogg" },
        { "flac", "audio/flac" },
        { "aac", "audio/aac" },
        { "m4a", "audio/mp4" },
        { "m4b", "audio/mp4" },
        { "weba", "audio/webm" },
        { "mid", "audio/midi" },
        { "midi", "audio/midi" },
        { "kar", "audio/midi" },
        { "aiff", "audio/aiff" },
        { "aif", "audio/aiff" },
        { "au", "audio/basic" },
        { "snd", "audio/basic" },
        { "ra", "audio/x-pn-realaudio" },
        { "ram", "audio/x-pn-realaudio" },
        { "wma", "audio/x-ms-wma" },
        { "ac3", "audio/ac3" },

        // ── Video ──
        { "mp4", "video/mp4" },
        { "m4v", "video/mp4" },
        { "webm", "video/webm" },
        { "mkv", "video/x-matroska" },
        { "mov", "video/quicktime" },
        { "qt", "video/quicktime" },
        { "avi", "video/x-msvideo" },
        { "wmv", "video/x-ms-wmv" },
        { "flv", "video/x-flv" },
        { "f4v", "video/x-f4v" },
        { "3gp", "video/3gpp" },
        { "3g2", "video/3gpp2" },
        { "3gpp", "video/3gpp" },
        { "ogv", "video/ogg" },
        { "mpg", "video/mpeg" },
        { "mpeg", "video/mpeg" },
        { "mpe", "video/mpeg" },
        { "m2v", "video/mpeg" },
        { "ts", "video/mp2t" },
        { "mts", "video/mp2t" },
        { "m2ts", "video/mp2t" },
        { "asf", "video/x-ms-asf" },

        // ── 3D / CAD models ──
        { "glb", "model/gltf-binary" },
        { "gltf", "model/gltf+json" },
        { "vrm", "model/gltf-binary" },
        { "stl", "model/stl" },
        { "obj", "text/plain" },
        { "fbx", "application/octet-stream" },
        { "pmx", "application/octet-stream" },
        { "pmd", "application/octet-stream" },
        { "ply", "application/octet-stream" },
        { "dae", "model/vnd.collada+xml" },
        { "x3d", "model/x3d+xml" },
        { "x3db", "model/x3d+binary" },
        { "x3dz", "model/x3d+vrml" },
        { "wrl", "model/vrml" },
        { "vrml", "model/vrml" },
        { "3ds", "application/x-3ds" },
        { "blend", "application/octet-stream" },
        { "usdz", "model/vnd.usdz+zip" },
        { "step", "application/step" },
        { "stp", "application/step" },
        { "iges", "application/iges" },
        { "igs", "application/iges" },

        // ── Web / static assets ──
        { "css", "text/css" },
        { "js", "text/javascript" },
        { "mjs", "text/javascript" },
        { "cjs", "text/javascript" },
        { "json", "application/json" },
        { "jsonc", "application/json" },
        { "geojson", "application/geo+json" },
        { "topojson", "application/json" },
        { "html", "text/html" },
        { "htm", "text/html" },
        { "xhtml", "application/xhtml+xml" },
        { "shtml", "text/html" },
        { "txt", "text/plain" },
        { "text", "text/plain" },
        { "xml", "application/xml" },
        { "xsl", "application/xslt+xml" },
        { "xslt", "application/xslt+xml" },
        { "rss", "application/rss+xml" },
        { "atom", "application/atom+xml" },
        { "svgx", "image/svg+xml" },
        { "wasm", "application/wasm" },

        // ── Fonts ──
        { "woff", "font/woff" },
        { "woff2", "font/woff2" },
        { "ttf", "font/ttf" },
        { "otf", "font/otf" },
        { "eot", "application/vnd.ms-fontobject" },
        { "ttc", "font/collection" },
        { "sfnt", "application/font-sfnt" },

        // ── Documents ──
        { "pdf", "application/pdf" },
        { "doc", "application/msword" },
        { "dot", "application/msword" },
        { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "docm", "application/vnd.ms-word.document.macroEnabled.12" },
        { "xls", "application/vnd.ms-excel" },
        { "xlt", "application/vnd.ms-excel" },
        { "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12" },
        { "xlsb", "application/vnd.ms-excel.sheet.binary.macroEnabled.12" },
        { "ppt", "application/vnd.ms-powerpoint" },
        { "pps", "application/vnd.ms-powerpoint" },
        { "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { "odt", "application/vnd.oasis.opendocument.text" },
        { "ods", "application/vnd.oasis.opendocument.spreadsheet" },
        { "odp", "application/vnd.oasis.opendocument.presentation" },
        { "odf", "application/vnd.oasis.opendocument.formula" },
        { "rtf", "application/rtf" },
        { "epub", "application/epub+zip" },
        { "mobi", "application/x-mobipocket-ebook" },
        { "azw", "application/vnd.amazon.ebook" },
        { "azw3", "application/vnd.amazon.ebook" },
        { "numbers", "application/vnd.apple.numbers" },
        { "pages", "application/vnd.apple.pages" },
        { "csv", "text/csv" },
        { "tsv", "text/tab-separated-values" },
        { "md", "text/markdown" },
        { "markdown", "text/markdown" },
        { "tex", "application/x-tex" },
        { "ltx", "application/x-tex" },

        // ── Archives ──
        { "zip", "application/zip" },
        { "zipx", "application/zip" },
        { "rar", "application/vnd.rar" },
        { "tar", "application/x-tar" },
        { "gz", "application/gzip" },
        { "tgz", "application/gzip" },
        { "bz2", "application/x-bzip2" },
        { "bz", "application/x-bzip" },
        { "xz", "application/x-xz" },
        { "zst", "application/zstd" },
        { "lz4", "application/x-lz4" },
        { "lz", "application/x-lzip" },
        { "7z", "application/x-7z-compressed" },
        { "cab", "application/vnd.ms-cab-compressed" },
        { "deb", "application/vnd.debian.binary-package" },
        { "rpm", "application/x-rpm" },
        { "iso", "application/x-iso9660-image" },

        // ── Source code / text formats ──
        { "c", "text/x-c" },
        { "h", "text/x-c" },
        { "cc", "text/x-c++" },
        { "cpp", "text/x-c++" },
        { "cxx", "text/x-c++" },
        { "hpp", "text/x-c++" },
        { "hh", "text/x-c++" },
        { "cs", "text/x-csharp" },
        { "java", "text/x-java" },
        { "go", "text/x-go" },
        { "rs", "text/x-rust" },
        { "rb", "text/x-ruby" },
        { "php", "text/x-php" },
        { "php3", "text/x-php" },
        { "phtml", "text/x-php" },
        { "py", "text/x-python" },
        { "pyw", "text/x-python" },
        { "pyc", "application/x-python-code" },
        { "ipynb", "application/x-ipynb+json" },
        { "sh", "text/x-sh" },
        { "bash", "text/x-sh" },
        { "zsh", "text/x-sh" },
        { "ksh", "text/x-sh" },
        { "pl", "text/x-perl" },
        { "pm", "text/x-perl" },
        { "lua", "text/x-lua" },
        { "sql", "text/x-sql" },
        { "swift", "text/x-swift" },
        { "kt", "text/x-kotlin" },
        { "kts", "text/x-kotlin" },
        { "scala", "text/x-scala" },
        { "r", "text/x-r" },
        { "dart", "text/x-dart" },
        // 注意：".ts" 在本多媒体 App 中优先作为 MPEG 传输流（video/mp2t，见 Video 段），
        // TypeScript 源码映射在此有意省略以避免与该键冲突。
        { "tsx", "text/x-typescript" },
        { "jsx", "text/jsx" },
        { "vue", "text/x-vue" },
        { "yml", "application/yaml" },
        { "yaml", "application/yaml" },
        { "toml", "application/toml" },
        { "ini", "text/plain" },
        { "cfg", "text/plain" },
        { "conf", "text/plain" },
        { "properties", "text/plain" },
        { "log", "text/plain" },
        { "env", "text/plain" },

        // ── Executables / binaries ──
        { "exe", "application/octet-stream" },
        { "dll", "application/octet-stream" },
        { "so", "application/octet-stream" },
        { "dylib", "application/octet-stream" },
        { "bin", "application/octet-stream" },
        { "apk", "application/vnd.android.package-archive" },
        { "aab", "application/vnd.android.package-archive" },
        { "msix", "application/vnd.ms-appx" },
        { "appx", "application/vnd.ms-appx" },
        { "jar", "application/java-archive" },
        { "war", "application/java-archive" },
        { "ear", "application/java-archive" },
        { "class", "application/java-vm" },

        // ── Data / databases ──
        { "sqlite", "application/vnd.sqlite3" },
        { "sqlite3", "application/vnd.sqlite3" },
        { "db", "application/octet-stream" },
        { "mdb", "application/x-msaccess" },
        { "accdb", "application/x-msaccess" },
        { "parquet", "application/parquet" },
        { "avro", "application/avro" },
        { "h5", "application/x-hdf5" },
        { "hdf5", "application/x-hdf5" },

        // ── Certificates / keys ──
        { "pem", "application/x-pem-file" },
        { "crt", "application/x-x509-ca-cert" },
        { "cer", "application/pkix-cert" },
        { "der", "application/x-x509-ca-cert" },
        { "key", "application/pkcs8" },
        { "pfx", "application/x-pkcs12" },
        { "p12", "application/x-pkcs12" },
        { "jks", "application/x-java-keystore" },

        // ── Misc / containers ──
        { "mht", "multipart/related" },
        { "mhtml", "multipart/related" },
        { "eml", "message/rfc822" },
        { "ics", "text/calendar" },
        { "ifb", "text/calendar" },
        { "vcf", "text/vcard" },
        { "vcard", "text/vcard" },
        { "gpx", "application/gpx+xml" },
        { "kml", "application/vnd.google-earth.kml+xml" },
        { "kmz", "application/vnd.google-earth.kmz" },
        { "gml", "application/gml+xml" },
        { "shp", "application/x-esri-shape" },
        { "torrent", "application/x-bittorrent" },
        { "nfo", "text/x-nfo" },
        { "dat", "application/octet-stream" },
        { "tmp", "application/octet-stream" },
    };

    /// <summary>
    /// 根据文件名或扩展名返回 MIME 类型；未知类型回退 <see cref="Fallback"/>。
    /// 入参可为完整路径、纯文件名，或带/不带前导点的扩展名。
    /// </summary>
    public static string Get(string fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
            return Fallback;

        // 允许直接传不带点的扩展名（如 "jpg"）。
        var ext = fileNameOrExtension;
        if (ext.IndexOf('.') >= 0)
            ext = Path.GetExtension(fileNameOrExtension);

        ext = ext.TrimStart('.').ToLowerInvariant();
        return Map.TryGetValue(ext, out var mime) ? mime : Fallback;
    }

    /// <summary>
    /// 尝试按扩展名查找 MIME 类型。找到返回 true 并通过 <paramref name="mime"/> 输出；
    /// 未知扩展名返回 false 且 <paramref name="mime"/> 为 <see cref="Fallback"/>。
    /// </summary>
    public static bool TryGet(string fileNameOrExtension, out string mime)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            mime = Fallback;
            return false;
        }

        var ext = fileNameOrExtension;
        if (ext.IndexOf('.') >= 0)
            ext = Path.GetExtension(fileNameOrExtension);

        ext = ext.TrimStart('.').ToLowerInvariant();
        if (Map.TryGetValue(ext, out var found))
        {
            mime = found;
            return true;
        }

        mime = Fallback;
        return false;
    }
}
