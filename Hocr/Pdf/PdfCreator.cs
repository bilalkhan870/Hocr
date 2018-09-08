using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Hocr.Enums;
using Hocr.HocrElements;
using Hocr.ImageProcessors;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Font = iTextSharp.text.Font;
using Image = iTextSharp.text.Image;
using Rectangle = iTextSharp.text.Rectangle;
namespace Hocr.Pdf
{
    public delegate Image ProcessImageForDisplay(System.Drawing.Image image);

    public delegate Bitmap ProcessImageForOcr(System.Drawing.Image image);

    internal class PdfCreator : IDisposable
    {
        private Document _doc;
        private HDocument _hDoc;

        private PdfWriter _writer;

        private readonly OcrController _ocrController;

        public PdfCreator(string newPdf,string tesseractPath)
        {
            _ocrController = new OcrController(tesseractPath);
            PdfSettings = new PdfSettings();
            PdfFilePath = newPdf;
            SetupDocumentWriter(newPdf);
            _hDoc = new HDocument();
            
        }

        public PdfCreator(string newPdf, string hocrFilePath,string sessionName, string tesseractPath)
        {
            _ocrController = new OcrController(tesseractPath);
            PdfSettings = new PdfSettings();
            PdfFilePath = newPdf;
            SetupDocumentWriter(newPdf);
            _hDoc = new HDocument();

            AddHocrFile(hocrFilePath,sessionName);
        }

        public PdfCreator(PdfSettings settings, string newPdf, string tesseractPath)
        {
            _ocrController = new OcrController(tesseractPath);
            PdfSettings = settings;
            PdfFilePath = newPdf;
            SetupDocumentWriter(newPdf);
            _hDoc = new HDocument();
        }

        public string PdfFilePath { get; }
        public PdfSettings PdfSettings { get; set; }

        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                _doc.Dispose();
                _writer.Dispose();
                _doc = null;
                _writer = null;
                GC.Collect();
            }
            catch
            {
                //
            }
        }

        #endregion

        public void AddHocrFile(string hocrFilePath,string sessionName)
        {
            HDocument doc = new HDocument();
            doc.AddFile(hocrFilePath);

            foreach (HPage p in doc.Pages)
            {
                Stream s = File.OpenRead(p.ImageFile);
                System.Drawing.Image image = System.Drawing.Image.FromStream(s);
                Guid objGuid = image.FrameDimensionsList[0];
                FrameDimension frameDim = new FrameDimension(objGuid);
                image.SelectActiveFrame(frameDim, p.ImageFrameNumber);
                System.Drawing.Image img = ImageProcessor.GetAsBitmap(image, PdfSettings.Dpi);
                AddPage(p, img,sessionName);
            }
        }

        /// <summary>
        ///     If adding an image directly, don't forget to call CreatePage
        /// </summary>
        /// <param name="image"></param>
        /// <param name="sessionName"></param>
        private void AddImage(System.Drawing.Image image,string sessionName)
        {
            try
            {
                if (OnProcessImageForDisplay != null)
                {
                    AddImage(OnProcessImageForDisplay(image));
                    return;
                }

                Bitmap bmp = ImageProcessor.GetAsBitmap(image, PdfSettings.Dpi);
                Image i = GetImageForPdf(bmp,sessionName);
                AddImage(i);
            }
            catch (Exception x)
            {
                Debug.WriteLine(x.Message);
                throw;
            }
        }

        private void AddImage(Image image)
        {
            try
            {
                //Getting Width of the image width adding the page right & left margin
                float width = image.Width / PdfSettings.Dpi * 72;

                //Getting Height of the image height adding the page top & bottom margin
                float height = image.Height / PdfSettings.Dpi * 72;

                //Creating pdf rectangle with the specified height & width for page size declaration
                Rectangle r = new Rectangle(width, height);

                /*you __MUST__ call SetPageSize() __BEFORE__ calling NewPage()
                * AND __BEFORE__ adding the image to the document
                */

                //Changing the page size of the pdf document based on the rectangle defined
                _doc.SetPageSize(PdfSettings.PdfPageSize ?? r);
                image.SetAbsolutePosition(0, 0);
                image.ScaleAbsolute(_doc.PageSize.Width, _doc.PageSize.Height);
                _doc.NewPage();
                _doc.Add(image);
                GC.Collect();
            }
            catch (Exception x)
            {
                Debug.WriteLine(x.Message);
                throw;
            }
        }
        
        public void AddPage(HPage page, System.Drawing.Image pageImage,string sessionName)
        {
            AddImage(pageImage,sessionName);
            WriteUnderlayContent(page);
        }

        public void AddPage(string imagePath, PdfMode mode,string sessionName)
        {
            AddPage(System.Drawing.Image.FromFile(imagePath), mode,sessionName);
        }

        public void AddPage(System.Drawing.Image image, PdfMode mode,string sessionName)
        {
            Guid objGuid = image.FrameDimensionsList[0];
            FrameDimension frameDim = new FrameDimension(objGuid);
            int frameCount = image.GetFrameCount(frameDim);
            for (int i = 0; i < frameCount; i++)
            {

                Bitmap img;

                image.SelectActiveFrame(frameDim, i);

                if (image is Bitmap == false)
                    img = ImageProcessor.GetAsBitmap(image, PdfSettings.Dpi);
                else
                    img = (Bitmap) image;

                img.SetResolution(PdfSettings.Dpi, PdfSettings.Dpi);

                switch (mode)
                {
                    case PdfMode.ImageOnly:
                        AddImage(image,sessionName);
                        break;
                    case PdfMode.Ocr:
                        try
                        {
                            AddImage(image,sessionName);

                            if (OnProcessImageForOcr != null)
                                img = OnProcessImageForOcr(img);
                            _ocrController.AddToDocument( PdfSettings.Language, image, ref _hDoc,sessionName);
                            HPage page = _hDoc.Pages[_hDoc.Pages.Count - 1];
                            WriteUnderlayContent(page);
                        }
                        catch (Exception)
                        {
                            //string message = x.Message;
                        }
                        break;
                    case PdfMode.TextOnly:
                        try
                        {
                            _doc.NewPage();
                            _ocrController.AddToDocument(PdfSettings.Language, image, ref _hDoc,sessionName);
                            HPage page = _hDoc.Pages[_hDoc.Pages.Count - 1];
                            WriteDirectContent(page);
                        }
                        catch (Exception)
                        {
                            //
                        }
                        break;
                    case PdfMode.DrawBlocks:
                        try
                        {
                            _ocrController.AddToDocument( PdfSettings.Language, image, ref _hDoc,sessionName);
                            HPage page = _hDoc.Pages[_hDoc.Pages.Count - 1];
                            WritePageDrawBlocks(image, page,sessionName);
                        }
                        catch (Exception)
                        {
                            //
                        }
                        break;
                    case PdfMode.Debug:
                        try
                        {
                            _ocrController.AddToDocument( PdfSettings.Language, image, ref _hDoc,sessionName);
                            HPage page = _hDoc.Pages[_hDoc.Pages.Count - 1];
                            WritePageDrawBlocks(image, page,sessionName);
                            WriteDirectContent(page);
                        }
                        catch (Exception)
                        {
                            //
                        }
                        break;
                }

                img.Dispose();
            }
        }

        public void AddPage(System.Drawing.Image image,string sessionName)
        {
            Guid objGuid = image.FrameDimensionsList[0];
            FrameDimension frameDim = new FrameDimension(objGuid);
            int frameCount = 0;
            try
            {
                frameCount = image.GetFrameCount(frameDim);
            }
            catch (Exception)
            {
                Bitmap img;
                if (image is Bitmap == false)
                    img = ImageProcessor.GetAsBitmap(image,
                        PdfSettings.Dpi);
                else
                    img = (Bitmap) image;
                img.SetResolution(PdfSettings.Dpi, PdfSettings.Dpi);

                AddImage(img,sessionName);
            }
            for (int i = 0; i < frameCount; i++)
            {
               Bitmap img;

                image.SelectActiveFrame(frameDim, i);

                if (image is Bitmap == false)
                    img = ImageProcessor.GetAsBitmap(image, PdfSettings.Dpi);
                else
                    img = (Bitmap) image;

                img.SetResolution(PdfSettings.Dpi, PdfSettings.Dpi);
                AddImage(image,sessionName);
                img.Dispose();
            }
        }

        public void AddPdf(string pdfFile, string bookMarkDesc, string id)
        {
            if (!File.Exists(pdfFile))
                return;

            iTextSharp.text.pdf.PdfReader reader = new iTextSharp.text.pdf.PdfReader(pdfFile);

            PdfContentByte cb = _writer.DirectContent;
            PdfOutline root = cb.RootOutline;

            for (int i = 1; i <= reader.NumberOfPages; i++)
            {
                if (i > reader.NumberOfPages)
                    break;

                PdfImportedPage page = _writer.GetImportedPage(reader, i);
                _doc.SetPageSize(reader.GetPageSize(i));
                _doc.NewPage();

                int rot = reader.GetPageRotation(i);

                if (rot == 90 || rot == 270)
                    cb.AddTemplate(page, 0, -1.0F, 1.0F, 0, 0, reader.GetPageSizeWithRotation(i).Height);
                else
                    cb.AddTemplate(page, 1.0F, 0, 0, 1.0F, 0, 0);

                if (i != 1)
                    continue;

                _doc.Add(new Chunk(bookMarkDesc).SetLocalDestination(id));
                // ReSharper disable once ObjectCreationAsStatement
                 new PdfOutline(root, PdfAction.GotoLocalPage(id, false), bookMarkDesc);
            }
            reader.Close();
        }

        public void AddPdf(string pdfFile)
        {
            if (!File.Exists(pdfFile))
                return;

            iTextSharp.text.pdf.PdfReader reader = new iTextSharp.text.pdf.PdfReader(pdfFile);

            PdfContentByte cb = _writer.DirectContent;
            for (int i = 1; i <= reader.NumberOfPages; i++)
            {
                if (i > reader.NumberOfPages)
                    break;

                _doc.NewPage();
                PdfImportedPage page = _writer.GetImportedPage(reader, i);

                int rot = reader.GetPageRotation(i);

                if (rot == 90 || rot == 270)
                    cb.AddTemplate(page, 0, -1.0F, 1.0F, 0, 0, reader.GetPageSizeWithRotation(i).Height);
                else
                    cb.AddTemplate(page, 1.0F, 0, 0, 1.0F, 0, 0);
            }
            reader.Close();
        }

        public void AddPdf(byte[] pdfPage)
        {
            iTextSharp.text.pdf.PdfReader reader = new iTextSharp.text.pdf.PdfReader(pdfPage);

            PdfContentByte cb = _writer.DirectContent;
            for (int i = 1; i <= reader.NumberOfPages; i++)
            {
                if (i > reader.NumberOfPages)
                    break;

                //set the current page size using the source page
                _doc.SetPageSize(reader.GetPageSize(i));
                _doc.NewPage();

                PdfImportedPage page = _writer.GetImportedPage(reader, i);

                int rot = reader.GetPageRotation(i);

                if (rot == 90 || rot == 270)
                    cb.AddTemplate(page, 0, -1.0F, 1.0F, 0, 0, reader.GetPageSizeWithRotation(i).Height);
                else
                    cb.AddTemplate(page, 1.0F, 0, 0, 1.0F, 0, 0);
            }
            reader.Close();
        }

        private Image GetImageForPdf(Bitmap image,string sessionName)
        {
            Image i = null;

            switch (PdfSettings.ImageType)
            {
                case PdfImageType.Tif:
                    i = Image.GetInstance(ImageProcessor.ConvertToCcittFaxTiff(image), ImageFormat.Tiff);
                    break;
                case PdfImageType.Png:
                    i = Image.GetInstance(ImageProcessor.ConvertToImage(image, "PNG", PdfSettings.ImageQuality, PdfSettings.Dpi),
                        ImageFormat.Png);
                    break;
                case PdfImageType.Jpg:
                    i = Image.GetInstance(ImageProcessor.ConvertToImage(image, "JPEG", PdfSettings.ImageQuality, PdfSettings.Dpi),
                        ImageFormat.Jpeg);
                    break;
                case PdfImageType.Bmp:
                    i = Image.GetInstance(ImageProcessor.ConvertToImage(image, "BMP", PdfSettings.ImageQuality, PdfSettings.Dpi),
                        ImageFormat.Bmp);
                    break;
                case PdfImageType.JBig2:
                    JBig2 jbig = new JBig2();
                    i = jbig.ProcessImage(image, sessionName);
                    break;
            }
            return i;
        }

        public event ProcessImageForDisplay OnProcessImageForDisplay;
        public event ProcessImageForOcr OnProcessImageForOcr;

        public void SaveAndClose()
        {
            try
            {
                if (_doc.PageNumber == 0)
                    _doc.NewPage();

                _writer.CompressionLevel = 100;
                _writer.SetFullCompression();

                _doc.Close();
            }
            catch (Exception)
            {
                //
            }
        }

        private void SetupDocumentWriter(string fileName)
        {
            _doc = new Document();

            _doc.SetMargins(0, 0, 0, 0);

            try
            {
                _writer = PdfWriter.GetInstance(_doc, new FileStream(fileName, FileMode.Create));
            }
            catch (Exception )
            {
                //Throw away.
            }
            

            _writer.SetMargins(0, 0, 0, 0);
            _doc.Open();

            if (PdfSettings == null)
                return;
            _doc.AddAuthor(PdfSettings.Author);
            _doc.AddTitle(PdfSettings.Title);
            _doc.AddSubject(PdfSettings.Subject);
            _doc.AddKeywords(PdfSettings.Keywords);
        }

        private void WriteDirectContent(HPage page)
        {
            List<HLine> allLines = page.Paragraphs.SelectMany(para => para.Lines).ToList();
            foreach (HParagraph para in page.Paragraphs)
            foreach (HLine line in para.Lines)
            {
                line.CleanText();
                if (line.Text.Trim() == string.Empty)
                    continue;

                BBox b = BBox.ConvertBBoxToPoints(line.BBox, PdfSettings.Dpi);

                if (b.Height > 28)
                    continue;

                PdfContentByte cb = _writer.DirectContent;

                BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, false);
                Font font = new Font(baseFont);
                if (!string.IsNullOrEmpty(PdfSettings.FontName))
                {
                    string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), PdfSettings.FontName);
                    baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                    font = new Font(baseFont);
                }

                float h = 9;

                float fontSize = allLines.Select(x => x.BBox.Height).Average() / PdfSettings.Dpi * 72.0f; // Math.Ceiling(b.Height);

                if ((int)fontSize == 0)
                    fontSize = 2;

                cb.BeginText();
                cb.SetFontAndSize(baseFont, (int) Math.Floor(fontSize) - 1);
                cb.SetTextMatrix(b.Left, _doc.PageSize.Height - b.Top - b.Height);
                cb.ShowText(line.Text);
                cb.EndText();
            }
        }

        private void WritePageDrawBlocks(System.Drawing.Image img, HPage page,string sessionName)
        {
            System.Drawing.Image himage = img;

            Bitmap rectCanvas = new Bitmap(himage.Width, himage.Height);
            Graphics grPhoto = Graphics.FromImage(rectCanvas);
            grPhoto.DrawImage(himage, new System.Drawing.Rectangle(0, 0, rectCanvas.Width, rectCanvas.Height), 0, 0, rectCanvas.Width, rectCanvas.Height,
                GraphicsUnit.Pixel);
            Graphics bg = Graphics.FromImage(rectCanvas);
            Pen bpen = new Pen(Color.Red, 3);
            Pen rpen = new Pen(Color.Blue, 3);
            Pen gpen = new Pen(Color.Green, 3);
            Pen ppen = new Pen(Color.HotPink, 3);


            foreach (HParagraph para in page.Paragraphs)
            {
                bg.DrawRectangle(gpen,
                    new System.Drawing.Rectangle(new Point((int) para.BBox.Left, (int) para.BBox.Top),
                        new Size((int) para.BBox.Width, (int) para.BBox.Height)));

                foreach (HLine line in para.Lines)
                {
                    foreach (HWord word in line.Words)
                        bg.DrawRectangle(rpen,
                            new System.Drawing.Rectangle(new Point((int) word.BBox.Left, (int) word.BBox.Top),
                                new Size((int) word.BBox.Width, (int) word.BBox.Height)));
                    bg.DrawRectangle(bpen,
                        new System.Drawing.Rectangle(new Point((int) line.BBox.Left, (int) line.BBox.Top),
                            new Size((int) line.BBox.Width, (int) line.BBox.Height)));
                }
            }
            IList<HLine> combinedLines = page.CombineSameRowLines();
            foreach (HLine l in combinedLines.Where(x => x.LineWasCombined))
                bg.DrawRectangle(ppen,
                    new System.Drawing.Rectangle(new Point((int) l.BBox.Left, (int) l.BBox.Top), new Size((int) l.BBox.Width, (int) l.BBox.Height)));

            AddImage(rectCanvas,sessionName);
        }

        public void WriteUnderlayContent(IList<HOcrClass> locations)
        {
            BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, false);
            Font font = new Font(baseFont);
            if (!string.IsNullOrEmpty(PdfSettings.FontName))
            {
                string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), PdfSettings.FontName);
                baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                font = new Font(baseFont);
            }

            foreach (HOcrClass c in locations)
            {
                BBox b = c.BBox;

                PdfContentByte cb = _writer.DirectContentUnder;

                cb.BeginText();
                cb.SetFontAndSize(baseFont, c.BBox.Height > 0 ? c.BBox.Height : 2);
                if (b.Format == UnitFormat.Point)
                    cb.SetTextMatrix(b.Left, b.Top - b.Height + 2);
                else
                    cb.SetTextMatrix(b.Left, _doc.PageSize.Height - b.Top - b.Height + 2);

                cb.ShowText(c.Text.Trim());
                cb.EndText();
            }
        }

        private void WriteUnderlayContent(HPage page)
        {
            
            foreach (HParagraph para in page.Paragraphs)
            foreach (HLine line in para.Lines)
            {
                if (PdfSettings.WriteTextMode == WriteTextMode.Word)
                {
                    line.AlignTops();

                    foreach (HWord c in line.Words)
                    {
                        c.CleanText();
                        BBox b = BBox.ConvertBBoxToPoints(c.BBox, PdfSettings.Dpi);

                        if (b.Height > 28)
                            continue;
                        PdfContentByte cb = _writer.DirectContentUnder;

                        BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, false);
                        Font font = new Font(baseFont);
                        if (!string.IsNullOrEmpty(PdfSettings.FontName))
                        {
                            string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), PdfSettings.FontName);
                            baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                            font = new Font(baseFont);
                        }

                        cb.BeginText();
                        cb.SetFontAndSize(baseFont, b.Height > 0 ? b.Height : 2);
                        cb.SetTextMatrix(b.Left, _doc.PageSize.Height - b.Top - b.Height + 2);
                        cb.SetWordSpacing(DocWriter.SPACE);
                        cb.ShowText(c.Text.Trim() + " ");
                        cb.EndText();
                    }
                }

                if (PdfSettings.WriteTextMode == WriteTextMode.Line)
                {
                    line.CleanText();
                    BBox b = BBox.ConvertBBoxToPoints(line.BBox, PdfSettings.Dpi);

                    if (b.Height > 28)
                        continue;

                    BBox lineBox = BBox.ConvertBBoxToPoints(line.BBox, PdfSettings.Dpi);
                    PdfContentByte cb = _writer.DirectContentUnder;

                    BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, false);
                    Font font = new Font(baseFont);

                    cb.BeginText();
                    cb.SetFontAndSize(baseFont, b.Height > 0 ? b.Height : 2);
                    cb.SetTextMatrix(b.Left, _doc.PageSize.Height - b.Top - b.Height + 2);
                    cb.SetWordSpacing(.25f);
                    cb.ShowText(line.Text);
                    cb.EndText();
                }

                if (PdfSettings.WriteTextMode != WriteTextMode.Character)
                    continue;
                {
                    line.AlignTops();

                    foreach (HWord word in line.Words)
                    {
                        word.AlignCharacters();
                        foreach (HChar c in word.Characters)
                        {
                            BBox b = BBox.ConvertBBoxToPoints(c.BBox, PdfSettings.Dpi);
                            BBox lineBox = BBox.ConvertBBoxToPoints(c.BBox, PdfSettings.Dpi);
                            PdfContentByte  cb = _writer.DirectContentUnder;

                            BaseFont baseFont = BaseFont.CreateFont(BaseFont.TIMES_ROMAN, BaseFont.WINANSI, false);
                            Font font = new Font(baseFont);

                            cb.BeginText();
                            cb.SetFontAndSize(baseFont, b.Height > 0 ? b.Height : 2);

                            cb.SetTextMatrix(b.Left, _doc.PageSize.Height - b.Top - b.Height + 2);
                            cb.SetCharacterSpacing(-1f);
                            cb.ShowText(c.Text.Trim());
                            cb.EndText();
                        }
                    }
                }
            }
        }
    }
}