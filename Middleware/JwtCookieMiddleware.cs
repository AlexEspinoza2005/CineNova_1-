namespace MovieApi.Middleware
{
    public class JwtCookieMiddleware
    {
        private readonly RequestDelegate _next;

        public JwtCookieMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var token = context.Request.Cookies["jwt_token"];

            if (!string.IsNullOrEmpty(token))
            {
                Console.WriteLine($"[Middleware] Token detectado en cookie para la ruta: {context.Request.Path}");
                if (!context.Request.Headers.ContainsKey("Authorization"))
                {
                    context.Request.Headers["Authorization"] = "Bearer " + token;
                    Console.WriteLine("[Middleware] Header 'Authorization' inyectado correctamente.");
                }
            }
            else
            {
                if (!context.Request.Path.StartsWithSegments("/Auth") && !context.Request.Path.StartsWithSegments("/Home") && context.Request.Path != "/")
                {
                    Console.WriteLine($"[Middleware] No hay token en cookie para ruta protegida: {context.Request.Path}");
                }
            }

            await _next(context);
        }
    }
}
