﻿
using System;
using System.IO;
using System.Threading.Tasks;
using Hocr.Enums;

namespace Hocr.Pdf
{
    public delegate void CompressorExceptionOccurred(PdfCompressor c, Exception x);

    public delegate string PreProcessImage(string bitmapPath);

    public class PdfCompressor
    {
        private string GhostScriptPath {
            get;
        }
        private string TesseractPath {
            get;
        }

        public PdfCompressor(string ghostScriptPath, string tesseractPath, PdfSettings settings = null)
        {
            TesseractPath = tesseractPath;
            GhostScriptPath = ghostScriptPath;
            PdfSettings = settings ?? new PdfSettings();
        }
        
        public PdfMode Mode {
            get; set;
        }
        
        public PdfSettings PdfSettings {
            get; set;
        }
        
        private bool CompressAndOcr(string sessionName, string inputFileName, string outputFileName)
        {

            PdfReader reader = new PdfReader(inputFileName, GhostScriptPath);
            PdfCreator writer = new PdfCreator(PdfSettings, outputFileName, TesseractPath)
            {
                PdfSettings =
                {
                    WriteTextMode = WriteTextMode.Word
                }
            };
            try
            {
                for (int i = 1; i <= reader.PageCount; i++)
                {
                    string img = reader.GetPageImage(i, true, sessionName);
                    if (OnPreProcessImage != null)
                        img = OnPreProcessImage(img);
                    writer.AddPage(img, PdfMode.Ocr, sessionName);
                }
                writer.SaveAndClose();
                writer.Dispose();
                reader.Dispose();
                return true;
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
                Console.WriteLine("Image not supported in " + Path.GetFileName(inputFileName) + ". Skipping");
                writer.SaveAndClose();
                writer.Dispose();
                reader.Dispose();
                GC.Collect();

                OnExceptionOccurred?.Invoke(this, x);
            }
            return false;
        }

        public event CompressorExceptionOccurred OnExceptionOccurred;

        /// <summary>
        ///     PreProcess the image before ocr and converting. Useful for deskewing, etc..
        /// </summary>
        public event PreProcessImage OnPreProcessImage;

        public async Task<byte[]> CreateSearchablePdf(byte[] fileData)
        {
            string sessionName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());

            TempData.Instance.CreateNewSession(sessionName);

            string inputDataFilePath = TempData.Instance.CreateTempFile(sessionName, ".pdf");
            string outputDataFilePath = TempData.Instance.CreateTempFile(sessionName, ".pdf");

            using (FileStream writer = new FileStream(inputDataFilePath, FileMode.Create, FileAccess.Write))
            {
                writer.Write(fileData, 0, fileData.Length);
                writer.Flush(true);
            }
            bool check = await (Task.Run(() => CompressAndOcr(sessionName, inputDataFilePath, outputDataFilePath)));
            byte[] outFile = File.ReadAllBytes(outputDataFilePath);
            TempData.Instance.DestroySession(sessionName);
            return outFile;
        }

    }
}