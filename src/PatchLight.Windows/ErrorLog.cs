namespace PatchLight.Windows;

// Same place the command line uses, so one file tells what went wrong last.
public static class ErrorLog
{
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight", "last-error.txt");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, ex.ToString());
        }
        catch (Exception)
        {
        }
    }
}
