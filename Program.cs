﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HeyRed.MarkdownSharp;
using Microsoft.Playwright;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace Poems
{
    class Poem
    {
        public string Title { get; set; }
        public int Index { get; set; }
        public string Link { get; set; }
        public DateTime PublicationDate { get; set; }
        public static List<string> Months = new List<string> { "", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        public string FilePath { get; set; }
        public int Page { get; set; }
    }

    class Program
    {
        static Markdown md = new Markdown();
        static string ContentTemplate = File.ReadAllText("Templates/content.html");
        static string TableOfContentsTemplate = File.ReadAllText("Templates/toc.html");
        static List<Poem> Poems { get; set; } = new List<Poem>();
        static Dictionary<string, List<Poem>> PoemsByDate = new Dictionary<string, List<Poem>>();
        static XFont Font = new XFont("Quattrocento", 12.0);

        static async Task Main(string[] args)
        {            
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Directory.Delete("Output", true);
            Directory.CreateDirectory("Output/Poems");  
            Directory.CreateDirectory("Output/Pdfs");  
            Directory.CreateDirectory("Output/Other");  
        
        // Parse Poems
            foreach (string dirpath in Directory.EnumerateDirectories("Poems"))
            foreach (string filepath in Directory.EnumerateFiles(dirpath))
            {
                AddPoem(filepath);
            }
            foreach (string filepath in Directory.EnumerateFiles("Poems"))
            {
                AddPoem(filepath);
            }

        // Build Index
            string indexHtml = string.Empty;
            foreach(Poem poem in Poems.OrderBy(p => p.Title))
            {
                indexHtml += $"<div>{poem.Link}</div>\n";
            }

        // Build Chronology
            string chronologyHtml = string.Empty;
            foreach(KeyValuePair<string, List<Poem>> kvp in PoemsByDate.OrderByDescending(kvp => kvp.Key))
            {
                chronologyHtml += $"<h3>{kvp.Key}</h3>\n";
                foreach(Poem poem in kvp.Value)
                {
                    chronologyHtml += $"<div>{poem.Link}</div>\n";
                }
            }

            string finalIndexHtml = File.ReadAllText("Templates/index.html")
                .Replace("{{index}}", indexHtml)
                .Replace("{{chronology}}", chronologyHtml);

            File.WriteAllText("index.html", finalIndexHtml);

            RenderOtherPage("Other/FAQ.md");
            RenderOtherPage("Other/Favorite Poems.md");
            RenderOtherPage("Other/Why Poetry.md");

            await RenderPdf("Poems.pdf");
        }

        static void AddPoem(string filepath)
        {
            var poem = new Poem();
            string filename = Path.GetFileNameWithoutExtension(filepath);
            string[] lines = File.ReadAllLines(filepath);
            
            filename = Regex.Replace(filename, "^[0-9][0-9] ", "");
            poem.Title = filename;
            
            try
            {
                poem.PublicationDate = System.DateTime.Parse(lines[1]);
                poem.Title = lines[0];
                lines[0] = $"### {lines[0]}";
                lines[1] = $"<p style='margin:0; margin-top: -1.25rem'><em><small><small>{lines[1]}</small></small></em></p>";                
            }
            catch(FormatException)
            {
                poem.PublicationDate = System.DateTime.Parse(lines[0]);
                lines[0] = $"<p style='margin:0; margin-top: -1.25rem'><em><small><small>{lines[0]}</small></small></em></p>";                
            }
            
            string content = String.Join("  \n", lines);
            string contentHtml = md.Transform(content);
            string finalPoemHtml = ContentTemplate.Replace("{{content}}", contentHtml);
            string finalPath = $"Output/Poems/{filename}.html";
            File.WriteAllText(finalPath, finalPoemHtml);

            poem.Link = $"<a href=\"{finalPath}\">{filename}</a>";
            poem.FilePath = finalPath;

            Poems.Add(poem);

        // Sort for chronology
            string key = poem.PublicationDate.ToString("yyyy-MM-dd");
            if (!PoemsByDate.ContainsKey(key))
                PoemsByDate[key] = new List<Poem>();
            PoemsByDate[key].Add(poem);
        }

        static void RenderOtherPage(string filepath)
        {
            string text = File.ReadAllText(filepath);
            string html = ContentTemplate.Replace("{{content}}", md.Transform(text));
            string htmlpath = $"Output/Other/{Path.GetFileNameWithoutExtension(filepath)}.html";
            File.WriteAllText(htmlpath, html);   
        }

        static async Task RenderPdf(string outpath)
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync();
            IPage page = await browser.NewPageAsync();

            using PdfDocument pdf = new PdfDocument();
            
            await RenderTableOfContents(page);
            using (PdfDocument tableOfContentsPdf = PdfReader.Open("Output/Pdfs/TableOfContents.pdf", PdfDocumentOpenMode.Import))
            {
                MergePdfs(tableOfContentsPdf, pdf);
            }

            PdfOutline contentsOutline = pdf.Outlines.Add("Contents", pdf.Pages[0]);
            PdfOutline poemsOutline = pdf.Outlines.Add("Poems", pdf.Pages[0]);
            foreach(KeyValuePair<string, List<Poem>> kvp in PoemsByDate.OrderByDescending(kvp => kvp.Key))
            {
                Directory.CreateDirectory($"Output/Pdfs/{kvp.Key}");
                foreach(Poem poem in kvp.Value)
                {
                    string pdfpath = $"Output/Pdfs/{kvp.Key}/{Path.GetFileNameWithoutExtension(poem.FilePath)}.pdf";
                    await page.GotoAsync("file:///" + Path.GetFullPath(poem.FilePath));
                    PagePdfOptions options = new PagePdfOptions 
                    { 
                        Path = pdfpath, 
                        Margin = new Margin { Left = "1in", Top = "1in", Right = "1in", Bottom = "1in" },
                    };
                    await page.PdfAsync(options);

                    using (PdfDocument poemPdf = PdfReader.Open(pdfpath, PdfDocumentOpenMode.Import))
                    {
                        poem.Page = pdf.PageCount;
                        MergePdfs(poemPdf, pdf);
                        poemsOutline.Outlines.Add(poem.Title, pdf.Pages[pdf.PageCount - poemPdf.PageCount]);
                    }
                }
            }

            await RenderTableOfContents(page);
            using (PdfDocument tableOfContentsPdf = PdfReader.Open("Output/Pdfs/TableOfContents.pdf", PdfDocumentOpenMode.Import))
            {
                int i = 0;
                foreach (PdfPage pdfPage in tableOfContentsPdf.Pages)
                {
                    pdf.Pages.RemoveAt(i);
                    PdfPage insertedPage = pdf.Pages.Insert(i, pdfPage);
                    AddPageNumber(insertedPage, i + 1);
                    ++i;
                }
                contentsOutline.DestinationPage = pdf.Pages[0];
                poemsOutline.DestinationPage = pdf.Pages[tableOfContentsPdf.PageCount];
            }

            pdf.Save(outpath);
        }

        static void MergePdfs(PdfDocument source, PdfDocument destination)
        {
            foreach (PdfPage pdfPage in source.Pages)
            {
                PdfPage addedPage = destination.AddPage(pdfPage);
                AddPageNumber(addedPage, destination.PageCount);
            }
        }

        static void AddPageNumber(PdfPage page, int pageNumber)
        {
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
            {
                double x = 0;
                double y = page.Height - Font.Height - new XUnit(0.5, XGraphicsUnit.Inch);
                double width = page.Width - new XUnit(0.5, XGraphicsUnit.Inch);
                double height = Font.Height;
                gfx.DrawString($"{pageNumber}", Font, XBrushes.Black, new XRect(x, y, width, height), XStringFormats.CenterRight);
            }
        }

        static async Task RenderTableOfContents(IPage page)
        {
            string toc = string.Empty;
            foreach(KeyValuePair<string, List<Poem>> kvp in PoemsByDate.OrderByDescending(kvp => kvp.Key))
            {
                toc += $"<div class='toc-section'>";
                toc += $"  <div class='toc-flex toc-section-header'>";
                toc += $"    <span>{kvp.Key}</span>";
                toc += $"    <span class='page'>{kvp.Value.FirstOrDefault()?.Page}</span>";
                toc += $"  </div>";
                foreach(Poem poem in kvp.Value)
                {
                    toc += $"  <div class='toc-flex toc-poem'>";
                    toc += $"    <span>{poem.Title}</span>";
                    toc += $"    <span class='page'>{poem.Page}</span>";
                    toc += $"  </div>";
                }
                toc += $"</div>";
            }

            string tocHtml = TableOfContentsTemplate.Replace("{{toc}}", toc);
            string filepath = "Output/Other/TableOfContents.html";
            File.WriteAllText(filepath, tocHtml);

            await page.GotoAsync("file:///" + Path.GetFullPath(filepath));

            string pdfPath = "Output/Pdfs/TableOfContents.pdf";
            PagePdfOptions options = new PagePdfOptions 
            { 
                Path = pdfPath,
                Margin = new Margin { Left = "1in", Top = "1in", Right = "1in", Bottom = "1in" },
            };         
            await page.PdfAsync(options);
        }
    }
}
