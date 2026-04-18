using System.IO;

namespace HearthSwing.Services;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string[] GetFiles(string path, string searchPattern, SearchOption searchOption);
    string[] GetDirectories(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] bytes);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive);
    void MoveDirectory(string source, string dest);
    void CopyFile(string source, string dest);
    void DeleteFile(string path);
    FileAttributes GetAttributes(string path);
    void SetAttributes(string path, FileAttributes attributes);
    DateTime GetLastWriteTime(string path);
    void SetLastWriteTime(string path, DateTime lastWriteTime);
    void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc);
    long GetFileLength(string path);
}
