namespace TaskOTime.Validation;

internal static class ThemeLifetimeSelfTest
{
    private sealed class Lifetime : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
        }
    }

    private sealed class WrongType
    {
        private readonly string disposed = "true";
        public override string ToString() => disposed;
    }

    internal static void Run()
    {
        foreach (var invalid in new object[] { new Lifetime(), new object(), new WrongType() })
        {
            var rejected = false;
            try { IdealStartupProbe.VerifyThemeDisposed(invalid); }
            catch (InvalidOperationException) { rejected = true; }
            WpfProbe.Assert(rejected, "Missing, false or non-boolean disposal state was accepted.");
        }
        var lifetime = new Lifetime();
        lifetime.Dispose();
        IdealStartupProbe.VerifyThemeDisposed(lifetime);
        Console.WriteLine("Synthetic theme lifetime guard passed: private bool disposed and three negative contracts; no product startup.");
    }
}
