using MpbkpuLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace MpbkpuLib.FileManagers
{
    public static class FileManagerFactory
    {
        public static IFileManager GetFileManager(string? path, out string newPosition)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            newPosition = path;
            // FTP
            // ftp://user:password@host/position
            if (path.StartsWith("ftp://")) {
                var m = Regex.Match(path, "ftp://(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var ftpFm = new Ftp();
                        ftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        ftpFm.Host = m.Groups[4].Value;
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            ftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        return ftpFm;
                    } else
                        throw new Exception("Invalid address");
                } else
                    throw new Exception("Invalid address");
            }

            // SFTP
            // sftp://user:password@host/position
            // sftp://user:keyfile=pathToFile@host/position
            if (path.StartsWith("sftp://")) {
                var m = Regex.Match(path, "sftp://(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var sftpFm = new Sftp();
                        sftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        sftpFm.Host = m.Groups[4].Value;
                        if (sftpFm.Credentials.Password.StartsWith("keyfile=")) {
                            sftpFm.PrivateKeyFile = sftpFm.Credentials.Password.Substring(8);
                            sftpFm.Credentials.Password = string.Empty;
                        }
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            sftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        return sftpFm;
                    } else
                        throw new Exception("Invalid address");
                } else
                    throw new Exception("Invalid address");
            }

            // SMB
            // smb://domain\user:password@host\position
            if (path.StartsWith("smb://")) {
                var m = Regex.Match(path, "smb://((.+)?\\\\(.+):(.+)@)?([^/]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var smbFm = new Smb(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = $"smb://{smbFm.Host}{m.Groups[6].Value}";
                        else
                            newPosition = $"smb://{smbFm.Host}/";
                        return smbFm;
                    } else
                        throw new Exception("Invalid address");
                } else
                    throw new Exception("Invalid address");
            }

            // OneDrive
            // onedrive://accessToken:refreshToken:position
            if (path.StartsWith("onedrive://")) {
                var m = Regex.Match(path, "onedrive://([^:]+)?:([^:]+)?:(.*)$");
                if (m.Success) {
                    string? accessToken = null;
                    string? refreshToken = null;
                    if (m.Groups.Count == 4) {
                        accessToken = m.Groups[1].Value;
                        refreshToken = m.Groups[2].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[3].Value))
                        newPosition = $"onedrive://{m.Groups[3].Value}";
                    else
                        newPosition = "onedrive:///";
                    var odFm = new OneDrive(accessToken, refreshToken);
                    return odFm;
                } else
                    throw new Exception("Invalid address");
            }

            return new FileSystem();
        } // GetFileManager
    } // FileManagerFactory
}