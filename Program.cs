using System.Diagnostics;
using System.Net;
using System.Text;

const string address = "http://localhost:8080/";
using var listener = new HttpListener();
listener.Prefixes.Add(address);
listener.Start();
Console.WriteLine($"Сервер слушает {address}");

while (true)
{
    HttpListenerContext context = await listener.GetContextAsync();
    try
    {
        await HandleRequestAsync(context);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Не удалось обработать запрос: {exception}");
    }
    finally
    {
        try
        {
            context.Response.Close();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось закрыть ответ: {exception}");
        }
    }
}

static Task HandleRequestAsync(HttpListenerContext context)
{
    string method = context.Request.HttpMethod;
    string path = context.Request.Url!.AbsolutePath;
    HttpListenerResponse response = context.Response;
    bool responseStarted = false;

    return LogAsync(() => HandleErrorsAsync(RouteAsync));

    async Task LogAsync(Func<Task> next)
    {
        Console.WriteLine($"→ {method} {path}");
        var timer = Stopwatch.StartNew();
        try
        {
            await next();
        }
        finally
        {
            Console.WriteLine($"← {method} {path} {response.StatusCode} ({timer.ElapsedMilliseconds} мс)");
        }
    }

    async Task HandleErrorsAsync(Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{method} {path}: {exception}");
            if (responseStarted)
            {
                response.Abort();
                return;
            }

            await SendAsync(500, "text/html", ErrorPage(500, "Внутренняя ошибка сервера"));
        }
    }

    async Task RouteAsync()
    {
        switch (method, path)
        {
            case ("GET", "/"):
                await SendAsync(200, "text/plain", "Сервер работает");
                break;
            case ("GET", "/tasks"):
                string filePath = Path.Combine(AppContext.BaseDirectory, "tasks.html");
                string html = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                await SendAsync(200, "text/html", html);
                break;
            default:
                await SendAsync(404, "text/html", ErrorPage(404, "Страница не найдена"));
                break;
        }
    }

    async Task SendAsync(int status, string mediaType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = $"{mediaType}; charset=utf-8";
        response.ContentLength64 = bytes.Length;

        // Первая запись может отправить заголовки до завершения WriteAsync.
        responseStarted = true;
        await response.OutputStream.WriteAsync(bytes);
    }
}

static string ErrorPage(int status, string message) =>
    $"""
    <!doctype html>
    <html lang="ru">
    <head>
        <meta charset="utf-8">
        <title>{status} {message}</title>
    </head>
    <body>
        <h1>{status} {message}</h1>
    </body>
    </html>
    """;
