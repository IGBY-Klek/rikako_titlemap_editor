using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using System.Reflection;
using System.IO;

namespace MapEditHelper
{
    public partial class Mainform : Form
    {
        private Bitmap? sourceImage;
        private Dictionary<string, Rectangle> tilePositions = new Dictionary<string, Rectangle>();
        private MenuStrip? menuStrip1; // Make menuStrip1 nullable
        private List<string[]> parsedRows = new List<string[]>();
        private TilePreviewForm? tilePreviewForm;
        private MapFileFormat currentMapFormat = MapFileFormat.Th04;
        private byte[] currentMapHeader = Array.Empty<byte>();

        private const int TILES_PER_ROW = 24;
        private const int TILES_PER_SECTION = 5;
        private int currentSection = 0;
        private int totalSections = 0;

        public Mainform()
        {
            InitializeComponent();

            // Ensure window icon is loaded from bundled icon file
            TrySetAppIcon();
            
            // Existing button click handlers
            richTextBox1.TextChanged += RichTextBox1_TextChanged;
            
            // Set form properties for menu
            this.MainMenuStrip = menuStrip1;
        }

        private void Button1_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.bmp;*.png;*.jpg";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    sourceImage = new Bitmap(ofd.FileName);

                    InitializeTilePositions();
                    ShowTilePreview(sourceImage);
                    GenerateFromText(); // update preview if map text already exists
                }
            }
        }

        private void InitializeTilePositions()
        {
            if (sourceImage == null) return;

            tilePositions.Clear();
            int tileSize = 16;
            int tilesPerRow = sourceImage.Width / tileSize;
            int rows = sourceImage.Height / tileSize;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < tilesPerRow; x++)
                {
                    string hexValue = $"{y:X1}{x:X1}";
                    Rectangle tileRect = new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
                    tilePositions[hexValue] = tileRect;
                }
            }
        }

        private void GenerateFromText(bool showWarnings = false)
        {
            if (string.IsNullOrWhiteSpace(richTextBox1.Text))
            {
                parsedRows.Clear();
                totalSections = 0;
                currentSection = 0;
                ClearPreview();
                UpdateSectionLabel();
                return;
            }

            if (sourceImage == null)
                return;

            parsedRows = ParseInput(richTextBox1.Text);
            if (parsedRows.Count == 0)
            {
                ClearPreview();
                totalSections = 0;
                currentSection = 0;
                UpdateSectionLabel();
                if (showWarnings)
                {
                    MessageBox.Show("No rows found in the map text. Expected lines like 'Row { 0x00, 0x01, ... }'.", "No map rows detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            totalSections = (int)Math.Ceiling(parsedRows.Count / (double)TILES_PER_SECTION);
            currentSection = 0; // Reset to first section
            UpdatePreview();
            UpdateSectionLabel(); // Update the section label
        }

        private void UpdatePreview()
        {
            if (sourceImage == null || parsedRows.Count == 0) return;
            int tileSize = 16;

            using (Bitmap resultImage = new Bitmap(TILES_PER_ROW * tileSize, TILES_PER_SECTION * tileSize))
            using (Graphics g = Graphics.FromImage(resultImage))
            {
                g.Clear(Color.White); // Clear background

                int startRow = currentSection * TILES_PER_SECTION;
                int endRow = Math.Min(startRow + TILES_PER_SECTION, parsedRows.Count);

                for (int rowIndex = startRow; rowIndex < endRow; rowIndex++)
                {
                    string[] hexValues = parsedRows[rowIndex];
                    for (int i = 0; i < Math.Min(hexValues.Length, TILES_PER_ROW); i++)
                    {
                        string hex = hexValues[i].Trim().ToUpper().PadLeft(2, '0');
                        if (tilePositions.TryGetValue(hex, out Rectangle sourceRect))
                        {
                            int destX = i * tileSize;
                            int destY = (rowIndex - startRow) * tileSize;
                            Rectangle destRect = new Rectangle(destX, destY, tileSize, tileSize);
                            g.DrawImage(sourceImage, destRect, sourceRect, GraphicsUnit.Pixel);
                        }
                    }
                }

                pictureBox2.Image?.Dispose();
                pictureBox2.Image = new Bitmap(resultImage);
                pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            }
        }

        private void ClearPreview()
        {
            pictureBox2.Image?.Dispose();
            pictureBox2.Image = null;
        }

        private List<string[]> ParseInput(string input)
        {
            List<string[]> result = new List<string[]>();

            // Match content between Row { and }, regardless of casing
            const string pattern = @"row\s*\{([^}]*)\}";
            MatchCollection matches = Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string rowContent = match.Groups[1].Value.Trim();
                    // Extract hex values, removing "0x" prefix (any casing) and normalizing to two digits
                    string[] hexValues = Regex.Matches(rowContent, @"(?:0x|0X)?([0-9a-fA-F]{1,2})")
                        .Cast<Match>()
                        .Select(m => m.Groups[1].Value.ToUpper().PadLeft(2, '0'))
                        .ToArray();

                    if (hexValues.Length > 0)
                    {
                        result.Add(hexValues);
                    }
                }
            }

            return result;
        }

        private void Button3_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Map Files|*.txt;*.map;*.bin|Text Files|*.txt|Binary Map Files|*.map;*.bin|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    LoadMapFile(ofd.FileName);
                    GenerateFromText(showWarnings: true);
                }
            }
        }

        private void LoadMapFile(string fileName)
        {
            byte[] bytes = File.ReadAllBytes(fileName);
            MapDecodeResult result = DecodeMapBytes(bytes);
            currentMapFormat = result.Format;
            currentMapHeader = result.Header;
            richTextBox1.Text = result.Text;
        }

        private MapDecodeResult DecodeMapBytes(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return new MapDecodeResult(string.Empty, MapFileFormat.Th04, Array.Empty<byte>());
            }

            if (TryDecodeTextMap(bytes, out string text))
            {
                return new MapDecodeResult(text, MapFileFormat.Text, Array.Empty<byte>());
            }

            if (TryExtractTh02MapData(bytes, out byte[] th02MapData))
            {
                return new MapDecodeResult(FormatRows(th02MapData.Select(b => b.ToString("X2"))), MapFileFormat.Th02, bytes.Take(4).ToArray());
            }

            if (TryExtractSizedMapData(bytes, headerSize: 8, out byte[] th05MapData))
            {
                return new MapDecodeResult(FormatRows(th05MapData.Select(b => b.ToString("X2"))), MapFileFormat.Th05, bytes.Take(8).ToArray());
            }

            if (TryExtractSizedMapData(bytes, headerSize: 2, out byte[] th04MapData))
            {
                return new MapDecodeResult(FormatRows(th04MapData.Select(b => b.ToString("X2"))), MapFileFormat.Th04, bytes.Take(2).ToArray());
            }

            return new MapDecodeResult(FormatRows(bytes.Select(b => b.ToString("X2"))), MapFileFormat.Raw, Array.Empty<byte>());
        }

        private bool TryExtractTh02MapData(byte[] bytes, out byte[] mapData)
        {
            mapData = Array.Empty<byte>();
            if (bytes.Length < 6)
            {
                return false;
            }

            int payloadSize = ReadUInt16LittleEndian(bytes, 0);
            if (payloadSize != bytes.Length - 4 || payloadSize < 2)
            {
                return false;
            }

            int mapSize = ReadUInt16LittleEndian(bytes, 4);
            if (mapSize > payloadSize - 2)
            {
                return false;
            }

            mapData = bytes.Skip(6).Take(mapSize).ToArray();
            return true;
        }

        private bool TryExtractSizedMapData(byte[] bytes, int headerSize, out byte[] mapData)
        {
            mapData = Array.Empty<byte>();
            if (bytes.Length < headerSize || headerSize < 2)
            {
                return false;
            }

            int mapSize = ReadUInt16LittleEndian(bytes, 0);
            if (mapSize != bytes.Length - headerSize)
            {
                return false;
            }

            mapData = bytes.Skip(headerSize).Take(mapSize).ToArray();
            return true;
        }

        private int ReadUInt16LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8);
        }

        private bool TryDecodeTextMap(byte[] bytes, out string text)
        {
            text = string.Empty;

            if (!LooksLikeText(bytes))
            {
                return false;
            }

            string decodedText = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            if (Regex.IsMatch(decodedText, @"\brow\s*\{", RegexOptions.IgnoreCase))
            {
                text = decodedText;
                return true;
            }

            string[] prefixedHexValues = Regex.Matches(decodedText, @"(?<![0-9A-Fa-f])(?:0x|0X|\$)([0-9A-Fa-f]{1,2})(?![0-9A-Fa-f])")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.ToUpper().PadLeft(2, '0'))
                .ToArray();
            if (prefixedHexValues.Length > 0)
            {
                text = FormatRows(prefixedHexValues);
                return true;
            }

            string[] plainHexValues = Regex.Matches(decodedText, @"(?<![0-9A-Fa-f])([0-9A-Fa-f]{2})(?![0-9A-Fa-f])")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.ToUpper())
                .ToArray();

            if (plainHexValues.Length == 0)
            {
                return false;
            }

            string textWithoutHexValues = Regex.Replace(decodedText, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{2}(?![0-9A-Fa-f])", string.Empty);
            bool hasOnlyHexSeparators = textWithoutHexValues.All(c => char.IsWhiteSpace(c) || c == ',' || c == ';' || c == '{' || c == '}');
            if (!hasOnlyHexSeparators)
            {
                return false;
            }

            text = FormatRows(plainHexValues);
            return true;
        }

        private bool LooksLikeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return true;
            }

            return bytes.All(b => b >= 0x20 || b == (byte)'\r' || b == (byte)'\n' || b == (byte)'\t');
        }

        private string FormatRows(IEnumerable<string> hexValues)
        {
            List<string> values = hexValues.ToList();
            List<string> rows = new List<string>();

            for (int i = 0; i < values.Count; i += TILES_PER_ROW)
            {
                rows.Add($"Row {{ {string.Join(", ", values.Skip(i).Take(TILES_PER_ROW).Select(v => $"0x{v}"))} }}");
            }

            return string.Join(Environment.NewLine, rows);
        }

        private void SaveEncodedMap_Click(object? sender, EventArgs e)
        {
            byte[] mapBytes = EncodeMapText(richTextBox1.Text);
            if (mapBytes.Length == 0)
            {
                MessageBox.Show("No tile codes found to save. Expected values like '0x00' in 'Row { ... }' lines.", "No map data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Binary Map Files|*.map;*.bin|All Files|*.*";
                sfd.DefaultExt = "map";
                sfd.FileName = "map.map";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllBytes(sfd.FileName, EncodeMapFile(mapBytes));
                }
            }
        }

        private byte[] EncodeMapText(string input)
        {
            return ParseInput(input)
                .SelectMany(row => row)
                .Select(hex => Convert.ToByte(hex, 16))
                .ToArray();
        }

        private byte[] EncodeMapFile(byte[] mapBytes)
        {
            return currentMapFormat switch
            {
                MapFileFormat.Th02 => EncodeTh02MapFile(mapBytes),
                MapFileFormat.Raw => mapBytes,
                _ => EncodeSizedMapFile(mapBytes, currentMapFormat == MapFileFormat.Th05 ? 8 : 2)
            };
        }

        private byte[] EncodeTh02MapFile(byte[] mapBytes)
        {
            byte[] result = new byte[mapBytes.Length + 6];
            byte[] header = currentMapHeader.Length == 4 ? currentMapHeader : new byte[4];
            Array.Copy(header, result, 4);
            WriteUInt16LittleEndian(result, 0, mapBytes.Length + 2);
            WriteUInt16LittleEndian(result, 4, mapBytes.Length);
            Array.Copy(mapBytes, 0, result, 6, mapBytes.Length);
            return result;
        }

        private byte[] EncodeSizedMapFile(byte[] mapBytes, int headerSize)
        {
            byte[] result = new byte[mapBytes.Length + headerSize];
            if (currentMapHeader.Length == headerSize)
            {
                Array.Copy(currentMapHeader, result, headerSize);
            }

            WriteUInt16LittleEndian(result, 0, mapBytes.Length);
            Array.Copy(mapBytes, 0, result, headerSize, mapBytes.Length);
            return result;
        }

        private void WriteUInt16LittleEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        // Add these methods to your Mainform class
        private void LoadTilemap_Click(object? sender, EventArgs e)
        {
            Button1_Click(sender, e); // Reuse existing functionality
        }

        private void LoadMap_Click(object? sender, EventArgs e)
        {
            Button3_Click(sender, e); // Reuse existing functionality
        }

        private void SaveMap_Click(object? sender, EventArgs e)
        {
            SaveEncodedMap_Click(sender, e);
        }

        private void Exit_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void About_Click(object? sender, EventArgs e)
        {
            MessageBox.Show("Map Edit Helper\nVersion 1.0", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Add these new methods
        private void UpdateSectionLabel()
        {
            lblSectionNumber.Text = totalSections > 0
                ? $"Section: {currentSection + 1}/{totalSections}"
                : "Section: 0/0";
            btnPrevSection.Enabled = currentSection > 0;
            btnNextSection.Enabled = currentSection < totalSections - 1;
        }

        private void NextSection_Click(object? sender, EventArgs e)
        {
            if (currentSection < totalSections - 1)
            {
                currentSection++;
                UpdatePreview();
                UpdateSectionLabel();
            }
        }

        private void PreviousSection_Click(object? sender, EventArgs e)
        {
            if (currentSection > 0)
            {
                currentSection--;
                UpdatePreview();
                UpdateSectionLabel();
            }
        }

        private void RichTextBox1_TextChanged(object? sender, EventArgs e)
        {
            GenerateFromText(); // auto-refresh when text changes
        }

        private void ShowTilePreview(Bitmap image)
        {
            tilePreviewForm?.Close();
            tilePreviewForm?.Dispose();

            tilePreviewForm = new TilePreviewForm();
            if (this.Icon != null)
            {
                tilePreviewForm.Icon = this.Icon;
            }
            tilePreviewForm.SetImage(image);
            tilePreviewForm.Show(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            tilePreviewForm?.Close();
            base.OnFormClosing(e);
        }

        private void TrySetAppIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "rikako.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                }
                else
                {
                    Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                }
            }
            catch
            {
                // Ignore icon failures to avoid crashing the UI
            }
        }

        private readonly record struct MapDecodeResult(string Text, MapFileFormat Format, byte[] Header);

        private enum MapFileFormat
        {
            Text,
            Raw,
            Th02,
            Th04,
            Th05
        }
    }
}
