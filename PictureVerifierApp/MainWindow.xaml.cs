using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Net.Http;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Media;

namespace PictureVerifierApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Observable collection for thumbnails
    private ObservableCollection<ImageThumbnailViewModel> imageThumbnails = new ObservableCollection<ImageThumbnailViewModel>();
    private const int MaxImages = 4;
    private string imgbbApiKey = null;
    private const string imgbbKeyGistUrl = "https://gist.githubusercontent.com/armahnii2000/0eff5ce1b5f2b981198edf126b5ef485/raw";

    // Thumbnail view model
    public class ImageThumbnailViewModel : INotifyPropertyChanged
    {
        public BitmapImage Image { get; set; }
        public string StatusIcon { get; set; } = "⏳"; // Default: hourglass
        public Brush StatusColor { get; set; } = Brushes.Gray;
        public string UploadedUrl { get; set; } = null;
        public double Progress { get; set; } = 0;
        public Visibility ProgressBarVisibility { get; set; } = Visibility.Visible;
        public event PropertyChangedEventHandler PropertyChanged;
        public void SetStatus(string icon, Brush color)
        {
            StatusIcon = icon;
            StatusColor = color;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
        }
        public void SetProgress(double value)
        {
            Progress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
        public void SetProgressBarVisibility(Visibility vis)
        {
            ProgressBarVisibility = vis;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressBarVisibility)));
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        ThumbnailItemsControl.ItemsSource = imageThumbnails;
        // Fetch the ImgBB API key at startup
        _ = FetchImgBBApiKeyAsync();
    }

    private async Task FetchImgBBApiKeyAsync()
    {
        try
        {
            using (var client = new HttpClient())
            {
                var key = await client.GetStringAsync(imgbbKeyGistUrl);
                imgbbApiKey = key.Trim();
            }
        }
        catch (Exception ex)
        {
            imgbbApiKey = null;
            MessageBox.Show($"Could not fetch ImgBB API key. Will use in-memory fallback.\n{ex.Message}", "ImgBB Key", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // '+' Upload button handler
    private void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (imageThumbnails.Count >= MaxImages)
        {
            MessageBox.Show($"You can upload up to {MaxImages} images.", "Limit reached", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenFileDialog dlg = new OpenFileDialog();
        dlg.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
        dlg.Title = "Select images";
        dlg.Multiselect = true;
        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                if (imageThumbnails.Count >= MaxImages) break;
                AddImageThumbnail(file);
            }
        }
    }

    // Upload area click handler (open file dialog)
    private void UploadArea_Click(object sender, MouseButtonEventArgs e)
    {
        UploadButton_Click(sender, e);
    }

    // Drag-and-drop support for upload area
    private void PromptArea_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && imageThumbnails.Count < MaxImages)
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PromptArea_DragLeave(object sender, DragEventArgs e)
    {
        // No overlay to hide
    }

    private void PromptArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
            {
                if (imageThumbnails.Count >= MaxImages) break;
                AddImageThumbnail(file);
            }
        }
    }

    // Add image to thumbnails and upload
    private void AddImageThumbnail(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            var vm = new ImageThumbnailViewModel { Image = bitmap };
            imageThumbnails.Add(vm);
            // Start upload
            _ = UploadImageToImgBBAsync(vm, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UploadImageToImgBBAsync(ImageThumbnailViewModel vm, string filePath)
    {
        if (!string.IsNullOrEmpty(imgbbApiKey))
        {
            try
            {
                vm.SetStatus("⏳", Brushes.Gray); // Uploading
                vm.SetProgressBarVisibility(Visibility.Visible);
                using (var client = new HttpClient())
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var ms = new MemoryStream())
                {
                    // Simulate progress
                    byte[] buffer = new byte[8192];
                    int read;
                    double total = 0;
                    double length = fs.Length;
                    while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await ms.WriteAsync(buffer, 0, read);
                        total += read;
                        vm.SetProgress((total / length) * 100);
                    }
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    var content = new MultipartFormDataContent();
                    content.Add(new StringContent(imgbbApiKey), "key");
                    content.Add(new StringContent(base64), "image");
                    var response = await client.PostAsync("https://api.imgbb.com/1/upload", content);
                    var respStr = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode && respStr.Contains("url"))
                    {
                        vm.SetStatus("✔", Brushes.Green);
                        vm.SetProgress(100);
                        vm.SetProgressBarVisibility(Visibility.Collapsed);
                    }
                    else
                    {
                        // Fallback to in-memory
                        vm.SetStatus("⚠", Brushes.Orange);
                        vm.SetProgressBarVisibility(Visibility.Collapsed);
                    }
                }
            }
            catch
            {
                // Fallback to in-memory
                vm.SetStatus("⚠", Brushes.Orange);
                vm.SetProgressBarVisibility(Visibility.Collapsed);
            }
        }
        else
        {
            // No API key, fallback to in-memory
            vm.SetStatus("⚠", Brushes.Orange);
            vm.SetProgressBarVisibility(Visibility.Collapsed);
        }
    }

    // Remove thumbnail handler
    private void RemoveThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ImageThumbnailViewModel vm)
        {
            imageThumbnails.Remove(vm);
        }
    }

    // Ask button handler
    private void AskButton_Click(object sender, RoutedEventArgs e)
    {
        string query = QueryTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            MessageBox.Show("Please enter a question.", "Missing question", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (imageThumbnails.Count == 0)
        {
            MessageBox.Show("Please upload at least one image.", "No images", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ChatStackPanel.Children.Clear();
        // For each image, generate a mock response
        for (int i = 0; i < imageThumbnails.Count; i++)
        {
            string response = GenerateMockResponse(query, i);
            AddChatBubble(imageThumbnails[i], query, response);
        }
        QueryTextBox.Text = string.Empty;
    }

    // Generate a mock response (could randomize or use query)
    private string GenerateMockResponse(string query, int idx)
    {
        string[] mockResponses = new string[]
        {
            "No visible defects detected.",
            "Image appears consistent with guidelines.",
            "Possible issue: background not uniform.",
            "Product looks new and unused.",
            "Unable to determine, please retake the photo."
        };
        // Pick a response based on index for demo
        return $"Q: {query}\nA: {mockResponses[idx % mockResponses.Length]}";
    }

    // Add a chat bubble (user and bot, ChatGPT style)
    private void AddChatBubble(ImageThumbnailViewModel image, string question, string response)
    {
        // User bubble (right)
        var userStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 5) };
        userStack.Children.Add(new TextBlock { Text = "You", FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Right });
        var userBubble = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 248, 198)),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 500
        };
        var userContent = new StackPanel { Orientation = Orientation.Horizontal };
        var img = new Image
        {
            Source = image.Image,
            Width = 60,
            Height = 60,
            Margin = new Thickness(0, 0, 10, 0),
            Stretch = System.Windows.Media.Stretch.UniformToFill
        };
        var txt = new TextBlock
        {
            Text = question,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontStyle = FontStyles.Italic,
            Foreground = System.Windows.Media.Brushes.Black,
            Width = 300
        };
        userContent.Children.Add(txt);
        userContent.Children.Add(img);
        userBubble.Child = userContent;
        userStack.Children.Add(userBubble);
        ChatStackPanel.Children.Add(userStack);

        // Bot bubble (left)
        var botStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 15) };
        botStack.Children.Add(new TextBlock { Text = "Bot", FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Left });
        var botBubble = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 500
        };
        var botContent = new TextBlock
        {
            Text = response,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Black,
            Width = 400
        };
        botBubble.Child = botContent;
        botStack.Children.Add(botBubble);
        ChatStackPanel.Children.Add(botStack);
    }
}