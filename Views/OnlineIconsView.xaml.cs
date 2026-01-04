using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RadiMenu.Models;
using RadiMenu.Services;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace RadiMenu.Views
{
    public partial class OnlineIconsView : System.Windows.Controls.UserControl
    {
        private readonly IconifyService _service;
        public event Action<string> IconSelected; // Returns "prefix:name"

        public OnlineIconsView()
        {
            InitializeComponent();
            _service = new IconifyService();
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void OnlineSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearch();
            }
        }

        private async System.Threading.Tasks.Task PerformSearch()
        {
            string query = OnlineSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            LoadingOverlay.Visibility = Visibility.Visible;
            OnlineIconsPanel.Children.Clear();
            StatusText.Text = "Buscando...";

            try
            {
                var result = await _service.SearchIconsAsync(query, 64);
                if (result != null && result.Icons.Any())
                {
                    StatusText.Text = $"Encontrados {result.Total} iconos (mostrando {result.Icons.Count})";
                    
                    // Render placeholders first
                    foreach (var iconName in result.Icons)
                    {
                        var border = CreateIconItem(iconName);
                        OnlineIconsPanel.Children.Add(border);
                        
                        // Async load the actual SVG data
                        // NOTE: In a real app, we should batch this request to api.iconify.design/icons?icons=a,b,c
                        // For MVP, loading one by one is acceptable but might be visible.
                        // Let's implement a 'Fire and Forget' load for each item to keep UI responsive
                        LoadIconImageAsync(border, iconName); 
                    }
                }
                else
                {
                    StatusText.Text = "No se encontraron iconos.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private Border CreateIconItem(string fullIconName)
        {
            var border = new Border
            {
                Width = 60,
                Height = 60,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                ToolTip = fullIconName,
                Tag = fullIconName
            };

            // Initial Content: Loading char or something
            var placeholder = new TextBlock
            {
                Text = "...",
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            border.Child = placeholder;
            border.MouseDown += Border_MouseDown;
            
            return border;
        }

        private async void LoadIconImageAsync(Border border, string iconName)
        {
            try
            {
                var data = await _service.GetIconDataAsync(iconName);
                if (data != null)
                {
                    // Convert SVG path to Geometry
                    string pathData = _service.ExtractPathData(data.Body);
                    if (!string.IsNullOrEmpty(pathData))
                    {
                        var path = new Path
                        {
                            Data = Geometry.Parse(pathData),
                            Fill = Brushes.White,
                            Stretch = Stretch.Uniform,
                            Width = 24,
                            Height = 24,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        
                        border.Child = path;
                        
                        // Normalize the display tag if needed
                        // border.ToolTip = $"{data.Prefix}:{data.Name}"; 
                    }
                }
            }
            catch
            {
                // Failed to load
                if (border.Child is TextBlock tb)
                {
                    tb.Text = "Err";
                    tb.Foreground = Brushes.Red;
                    tb.ToolTip = "Error al cargar icono";
                }
            }
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string iconName)
            {
                // Visual feedback managed by parent or here? 
                // Let's just fire event
                IconSelected?.Invoke(iconName);
            }
        }
    }
}
