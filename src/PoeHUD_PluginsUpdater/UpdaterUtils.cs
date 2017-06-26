using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SharpDX;

namespace PoeHUD_PluginsUpdater
{
    public class UpdaterUtils
    {
        public static string GetGitObjectChecksum(string file)
        {
            var bytes = File.ReadAllBytes(file);
            var blobString = "blob " + bytes.Length + "\0";
            var appendBytes = Encoding.Default.GetBytes(blobString);

            var newArray = new byte[appendBytes.Length + bytes.Length];

            Buffer.BlockCopy(appendBytes, 0, newArray, 0, appendBytes.Length);
            Buffer.BlockCopy(bytes, 0, newArray, appendBytes.Length, bytes.Length);

            var sha = new SHA1Managed();
            var hash = sha.ComputeHash(newArray);
            var stringBuilder = new StringBuilder();
            foreach (var b in hash)
            {
                stringBuilder.AppendFormat("{0:x2}", b);
            }
            return stringBuilder.ToString();
        }

        public static bool DrawButton(RectangleF rect, float borderWidth, Color boxColor, Color frameColor)
        {
            if (rect.Contains(PoeHUD_PluginsUpdater.Mouse_Pos))
                boxColor = Color.Lerp(boxColor, Color.White, 0.4f);

            DrawFrameBox(rect, borderWidth, boxColor, frameColor);
            if (!PoeHUD_PluginsUpdater.bMouse_Click) return false;
            return rect.Contains(PoeHUD_PluginsUpdater.Mouse_ClickPos);
        }

        public static void DrawFrameBox(RectangleF rect, float borderWidth, Color boxColor, Color frameColor)
        {
            PoeHUD_PluginsUpdater.UGraphics.DrawBox(rect, boxColor);
            PoeHUD_PluginsUpdater.UGraphics.DrawFrame(rect, borderWidth, frameColor);
        }

    }
}
