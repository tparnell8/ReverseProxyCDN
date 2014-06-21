﻿using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

public class MissingFileModule : IHttpModule
{
    private static readonly string[] _extensions = ConfigurationManager.AppSettings.Get("extensions").Split(' ');

    public void Init(HttpApplication context)
    {
        var asyncHelper = new EventHandlerTaskAsyncHelper(OnEndRequestAsync);
        context.AddOnEndRequestAsync(asyncHelper.BeginEventHandler, asyncHelper.EndEventHandler);
    }

    private async Task OnEndRequestAsync(object sender, EventArgs e)
    {
        HttpApplication application = (HttpApplication)sender;

        if (application == null)
            return;

        HttpContext context = application.Context;

        if (context.Response.StatusCode == 404)
        {
            string filePath = context.Request.FilePath;

            if (!_extensions.Contains(Path.GetExtension(filePath)))
                throw new HttpException(403, "File extension not supported");

            Uri url = GetRemoteUrl(context, filePath);
            await DownloadAndServeFile(context, filePath, url);
        }
    }

    private static Uri GetRemoteUrl(HttpContext context, string filePath)
    {
        int end = filePath.IndexOf("/", 1, StringComparison.Ordinal) - 1;

        if (end == -1)
            throw new HttpException(404, "File not found");

        string domain = filePath.Substring(1, end);
        string path = filePath.Substring(end + 1);
        Uri url;

        if (!Uri.TryCreate(context.Request.Url.Scheme + "://" + domain + path, UriKind.Absolute, out url))
            throw new HttpException(406, "Not accepted");

        return url;
    }

    private async Task DownloadAndServeFile(HttpContext context, string local, Uri remote)
    {
        FileInfo file = new FileInfo(context.Server.MapPath(local));

        using (WebClient client = new WebClient())
        {
            client.Headers.Add("User-Agent", "Reverse Proxy 1.0 (http://m82.be)");
            byte[] buffer = await client.DownloadDataTaskAsync(remote);

            await SaveFile(file, buffer);

            context.Response.BinaryWrite(buffer);
            context.Response.ContentType = client.ResponseHeaders["content-type"];
            context.Response.StatusCode = 200;
        }
    }

    private async Task SaveFile(FileInfo file, byte[] buffer)
    {
        file.Directory.Create();
        File.WriteAllBytes(file.FullName, buffer);

        using (FileStream sourceStream = new FileStream(file.FullName, FileMode.Truncate, FileAccess.Write, FileShare.None, 4096, true))
        {
            await sourceStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }

    public void Dispose()
    {
    }
}