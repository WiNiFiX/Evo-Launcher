using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace EvoLauncher;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private List<EvolutionInfo> evolutionFiles = new List<EvolutionInfo>();
    private System.Windows.Threading.DispatcherTimer timer;
    private Dictionary<string, string> customVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly string customVersionsFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_versions.json");

    public MainWindow()
    {
        InitializeComponent();
        Title = "Evolution Launcher";
        Topmost = true;
        LoadCustomVersions();
        Loaded += MainWindow_Loaded;
        
        // Set up timer to check running processes every 5 seconds
        timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanForEvolutionFiles();
        await Dispatcher.InvokeAsync(PositionBottomRight, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth;
        Top = workArea.Bottom - ActualHeight;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateButtonStates();
    }

    private async Task ScanForEvolutionFiles()
    {
        try
        {
            evolutionFiles.Clear();

            string searchPath = @"C:\Program Files (x86)\";
            if (Directory.Exists(searchPath))
            {
                var files = Directory.GetFiles(searchPath, "evolution.exe", SearchOption.AllDirectories);
                
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    // Deduplicate: skip if same path already added
                    if (!seenPaths.Add(file))
                        continue;

                    try
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(file);
                        string version = versionInfo.FileVersion ?? "1.0.0.0";
                        string md5Hash = CalculateMD5(file);

                        var evolutionInfo = new EvolutionInfo
                        {
                            FilePath = file,
                            Version = version,
                            MD5Hash = md5Hash,
                            FolderVersion = GetVersionFromFolderName(file)
                        };

                        if (customVersions.TryGetValue(file, out string? cv))
                            evolutionInfo.CustomVersion = cv;

                        evolutionFiles.Add(evolutionInfo);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not read version from {file}: {ex.Message}");
                    }
                }

                // Deduplicate by MD5 hash: same binary at multiple paths → keep the shortest path (closest to install root)
                evolutionFiles = evolutionFiles
                    .GroupBy(e => e.MD5Hash)
                    .Select(g => g.OrderBy(e => e.FilePath.Length).First())
                    .ToList();
            }

            // Sort by version number (highest first) - use DisplayVersion to include custom names
            evolutionFiles = evolutionFiles.OrderByDescending(e => ParseVersion(e.DisplayVersion)).ToList();
            
            CreateButtons();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning files: {ex.Message}");
        }
    }

    private void CreateButtons()
    {
        // Remove existing buttons (keep the title and status text)
        var buttonsToRemove = ButtonPanel.Children.OfType<Button>().ToList();
        foreach (var button in buttonsToRemove)
        {
            ButtonPanel.Children.Remove(button);
        }

        // Add buttons for each evolution.exe found
        var random = new Random();
        var styleNames = new[] { "JaggedButton1", "JaggedButton2", "JaggedButton3", "JaggedButton4" };
        
        foreach (var evolutionInfo in evolutionFiles)
        {
            var button = new Button
            {
                Content = $"Launch Evolution v{evolutionInfo.DisplayVersion}",
                Height = 50,
                Tag = evolutionInfo
            };
            
            // Randomly assign one of the jagged styles
            string randomStyleName = styleNames[random.Next(styleNames.Length)];
            button.Style = (Style)FindResource(randomStyleName);
            
            button.Click += LaunchButton_Click;
            button.MouseRightButtonUp += EditButton_RightClick;
            ButtonPanel.Children.Add(button);
        }

        UpdateButtonStates();
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is EvolutionInfo evolutionInfo)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = evolutionInfo.FilePath,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(evolutionInfo.FilePath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch {evolutionInfo.FilePath}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void UpdateButtonStates()
    {
        var runningProcesses = Process.GetProcessesByName("evolution");
        var runningFilePaths = new HashSet<string>();

        foreach (var process in runningProcesses)
        {
            try
            {
                if (process.MainModule != null)
                {
                    runningFilePaths.Add(process.MainModule.FileName);
                }
            }
            catch
            {
                // Ignore processes we can't access
            }
        }

        // Update button states - disable if the exact file path is running
        foreach (Button button in ButtonPanel.Children.OfType<Button>())
        {
            if (button.Tag is EvolutionInfo evolutionInfo)
            {
                button.IsEnabled = !runningFilePaths.Contains(evolutionInfo.FilePath);
            }
        }
    }

    private void LoadCustomVersions()
    {
        try
        {
            if (File.Exists(customVersionsFile))
            {
                string json = File.ReadAllText(customVersionsFile);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                customVersions = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading custom versions: {ex.Message}");
        }
    }

    private void SaveCustomVersions()
    {
        try
        {
            string json = JsonSerializer.Serialize(customVersions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(customVersionsFile, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving custom versions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string CalculateMD5(string filePath)
    {
        try
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 8); // First 8 chars
            }
        }
        catch
        {
            return "unknown";
        }
    }

    private void EditButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is EvolutionInfo evolutionInfo)
        {
            string currentVersion = evolutionInfo.CustomVersion ?? evolutionInfo.DisplayVersion;
            string? newVersion = ShowInputDialog(
                "Edit Version Name",
                $"Enter custom version name for:\n{evolutionInfo.FilePath}\n\nCurrent: {currentVersion}",
                currentVersion);

            if (!string.IsNullOrWhiteSpace(newVersion) && newVersion != currentVersion)
            {
                evolutionInfo.CustomVersion = newVersion;
                customVersions[evolutionInfo.FilePath] = newVersion;
                SaveCustomVersions();
                
                // Update button text
                button.Content = $"Launch Evolution v{evolutionInfo.DisplayVersion}";
            }
        }
    }

    private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
    {
        var inputWindow = new Window
        {
            Title = title,
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F1B14")!)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var textBlock = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5E6D3")!),
            FontFamily = new FontFamily("Georgia"),
            FontSize = 12
        };
        Grid.SetRow(textBlock, 0);
        grid.Children.Add(textBlock);

        var textBox = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(15, 5, 15, 15),
            Height = 35,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5E6D3")!),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F1B14")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#654321")!),
            BorderThickness = new Thickness(2),
            FontFamily = new FontFamily("Georgia"),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10)
        };
        
        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 32,
            Margin = new Thickness(5, 0, 0, 0),
            IsDefault = true,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B4513")!),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5E6D3")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#654321")!),
            BorderThickness = new Thickness(2),
            FontFamily = new FontFamily("Georgia"),
            FontWeight = FontWeights.Bold
        };
        okButton.Click += (s, e) => inputWindow.DialogResult = true;
        
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32,
            IsCancel = true,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B4513")!),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5E6D3")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#654321")!),
            BorderThickness = new Thickness(2),
            FontFamily = new FontFamily("Georgia"),
            FontWeight = FontWeights.Bold
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        inputWindow.Content = grid;
        
        bool? result = inputWindow.ShowDialog();
        return result == true ? textBox.Text : null;
    }

    private string? GetVersionFromFolderName(string filePath)
    {
        string? dir = System.IO.Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            string folderName = System.IO.Path.GetFileName(dir) ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(folderName, @"^Sage Evolution\s+([\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
            string? parent = System.IO.Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private Version ParseVersion(string versionString)
    {
        try
        {
            // Handle empty or null strings
            if (string.IsNullOrWhiteSpace(versionString))
                return new Version(0, 0, 0, 0);

            // If it looks like an MD5 hash (all hex characters), treat as lowest priority
            if (versionString.Length == 8 && versionString.All(c => "0123456789abcdef".Contains(char.ToLower(c))))
                return new Version(0, 0, 0, 1); // Slightly higher than default but very low

            // Try to extract version numbers from the string (handles custom names like "MyCustom 7.20.9.14")
            var versionMatch = System.Text.RegularExpressions.Regex.Match(versionString, @"(\d+(?:\.\d+){0,3})");
            if (versionMatch.Success)
            {
                versionString = versionMatch.Groups[1].Value;
            }
            else
            {
                // If no version pattern found, treat as lowest priority
                return new Version(0, 0, 0, 2);
            }

            // Split the version string and ensure we have exactly 4 parts
            var parts = versionString.Split('.');
            var versionParts = new int[4]; // Default to 0 for missing parts
            
            for (int i = 0; i < Math.Min(parts.Length, 4); i++)
            {
                if (int.TryParse(parts[i], out int part))
                {
                    versionParts[i] = part;
                }
            }
            
            // Create version with all 4 parts, padding with 0 where needed
            return new Version(versionParts[0], versionParts[1], versionParts[2], versionParts[3]);
        }
        catch
        {
            // If all parsing fails, return a default version
            return new Version(0, 0, 0, 0);
        }
    }
}

public class EvolutionInfo
{
    public required string FilePath { get; set; }
    public required string Version { get; set; }
    public string? CustomVersion { get; set; }
    public string? FolderVersion { get; set; }
    public required string MD5Hash { get; set; }
    public string DisplayVersion => CustomVersion
        ?? (Version != "1.0.0.0" && Version != "0.0.0.0" ? Version : null)
        ?? FolderVersion
        ?? MD5Hash;
}
