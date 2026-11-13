// =========================================================
//  ReporteSaludPdfService.cs
//  Genera el reporte de salud del sistema en PDF, mostrando
//  SOLO lo importante: el puntaje, su desglose, un resumen
//  rápido de CPU/RAM/Disco, los procesos más pesados y los
//  programas de inicio de alto impacto que están habilitados.
//
//  NO incluye la lista completa de procesos ni de programas de
//  inicio — para eso ya existe el reporte JSON (ReportService).
//
//  ⚠️ REQUIERE el paquete NuGet "QuestPDF":
//      dotnet add package QuestPDF
//  Es gratis para uso individual / empresas pequeñas bajo la
//  licencia "Community" (ya queda aceptada automáticamente
//  abajo, no hay que hacer nada más).
// =========================================================
using System;
using System.Collections.Generic;
using System.Linq;
using AplicativoAnalizador.Modelo;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AplicativoAnalizador.Servicios
{
    public class ReporteSaludPdfService
    {
        static ReporteSaludPdfService()
        {
            // Se acepta la licencia Community una sola vez por proceso.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// Arma el resumen de salud (SOLO lo importante) a partir de datos
        /// ya calculados en pantalla. No vuelve a calcular ni a leer nada
        /// del sistema, solo organiza lo que ya se tiene.
        /// </summary>
        public PuntajeSaludDatos ConstruirResumen(
            PuntajeSalud puntaje,
            double cpu, string cpuEstado,
            double ram, string ramEstado,
            double discoLibre, string discoEstado,
            IReadOnlyList<ProcesoInfo> procesos,
            IReadOnlyList<StartupItem> programasInicio)
        {
            // ✅ Top 5 procesos por CPU, sin contar los críticos del sistema
            //    (esos no le sirven de nada al usuario en el reporte).
            var topProcesos = procesos
                .Where(p => !p.EsCritico)
                .OrderByDescending(p => p.Cpu)
                .Take(5)
                .Select(p => new ProcesoDestacado { Nombre = p.Nombre, Cpu = p.Cpu, RamMb = p.RamMb })
                .ToList();

            // ✅ Solo los de impacto alto Y habilitados (los que sí valen
            //    la pena mencionar, no la lista completa de arranque).
            var altoImpacto = programasInicio
                .Where(p => p.Habilitado && p.Impacto == "Alto")
                .Take(5)
                .Select(p => new ProgramaInicioDestacado { Nombre = p.Nombre, Editor = p.Editor })
                .ToList();

            return new PuntajeSaludDatos
            {
                Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                PuntajeTotal = puntaje.Total,
                EstadoGeneral = puntaje.Estado,
                Desglose = puntaje.Desglose,
                Cpu = cpu,
                CpuEstado = cpuEstado,
                Ram = ram,
                RamEstado = ramEstado,
                DiscoLibre = discoLibre,
                DiscoEstado = discoEstado,
                ProgramasInicioHabilitados = programasInicio.Count(p => p.Habilitado),
                ProgramasInicioTotal = programasInicio.Count,
                ProcesosPesadosCantidad = procesos.Count(p => p.Cpu >= 25),
                ProcesosDestacados = topProcesos,
                ProgramasAltoImpacto = altoImpacto,
            };
        }

        /// <summary>
        /// Genera el archivo PDF en la ruta indicada.
        /// </summary>
        public void GenerarPdf(PuntajeSaludDatos datos, string rutaArchivo)
        {
            string colorPuntaje = ColorParaEstado(datos.EstadoGeneral);

            Document.Create(documento =>
            {
                documento.Page(pagina =>
                {
                    pagina.Size(PageSizes.A4);
                    pagina.Margin(36);
                    pagina.DefaultTextStyle(x => x.FontSize(11));

                    // ---------------- ENCABEZADO ----------------
                    pagina.Header().Column(col =>
                    {
                        col.Item().Text("ECLIPSE — Reporte de salud del sistema")
                            .FontSize(20).SemiBold();
                        col.Item().PaddingTop(2).Text($"Generado: {datos.Fecha}")
                            .FontSize(10).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    // ---------------- CONTENIDO ----------------
                    pagina.Content().PaddingTop(15).Column(col =>
                    {
                        col.Spacing(16);

                        // ---- Puntaje principal ----
                        col.Item().Row(fila =>
                        {
                            fila.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Puntaje de salud").FontSize(12).FontColor(Colors.Grey.Darken1);
                                c.Item().Text($"{datos.PuntajeTotal}/100").FontSize(36).Bold().FontColor(colorPuntaje);
                            });
                            fila.RelativeItem().AlignRight().AlignMiddle()
                                .Text(datos.EstadoGeneral).FontSize(16).Bold().FontColor(colorPuntaje);
                        });

                        // ---- Desglose por componente ----
                        col.Item().Element(c => SeccionTitulo(c, "Desglose por componente"));
                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(1);
                            });

                            foreach (var comp in datos.Desglose)
                            {
                                tabla.Cell().PaddingVertical(2).Text(comp.Nombre);
                                tabla.Cell().PaddingVertical(2).AlignRight().Text($"{comp.Puntos}/{comp.Maximo} pts");
                            }
                        });

                        // ---- Resumen rápido ----
                        col.Item().Element(c => SeccionTitulo(c, "Resumen rápido"));
                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });

                            tabla.Cell().Text($"CPU: {datos.Cpu:0}% ({datos.CpuEstado})");
                            tabla.Cell().Text($"RAM: {datos.Ram:0}% ({datos.RamEstado})");
                            tabla.Cell().Text($"Disco libre: {datos.DiscoLibre:0}% ({datos.DiscoEstado})");
                        });

                        // ---- Procesos más pesados ----
                        if (datos.ProcesosDestacados.Count > 0)
                        {
                            col.Item().Element(c => SeccionTitulo(c,
                                $"Procesos más pesados ({datos.ProcesosPesadosCantidad} con CPU ≥ 25%)"));

                            col.Item().Table(tabla =>
                            {
                                tabla.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(1);
                                    c.RelativeColumn(1);
                                });

                                tabla.Header(h =>
                                {
                                    h.Cell().Text("Proceso").Bold();
                                    h.Cell().AlignRight().Text("CPU").Bold();
                                    h.Cell().AlignRight().Text("RAM").Bold();
                                });

                                foreach (var p in datos.ProcesosDestacados)
                                {
                                    tabla.Cell().PaddingVertical(2).Text(p.Nombre);
                                    tabla.Cell().PaddingVertical(2).AlignRight().Text($"{p.Cpu:0.0}%");
                                    tabla.Cell().PaddingVertical(2).AlignRight().Text($"{p.RamMb} MB");
                                }
                            });
                        }
                        else
                        {
                            col.Item().Text("No hay procesos con uso alto de CPU en este momento.")
                                .FontColor(Colors.Grey.Darken1);
                        }

                        // ---- Programas de inicio ----
                        col.Item().Element(c => SeccionTitulo(c, "Programas de inicio"));
                        col.Item().Text($"{datos.ProgramasInicioHabilitados} de {datos.ProgramasInicioTotal} están habilitados al arrancar Windows.");

                        if (datos.ProgramasAltoImpacto.Count > 0)
                        {
                            col.Item().PaddingTop(4)
                                .Text("De impacto alto y habilitados actualmente:")
                                .FontSize(10).FontColor(Colors.Grey.Darken1);

                            foreach (var p in datos.ProgramasAltoImpacto)
                            {
                                col.Item().Text($"•  {p.Nombre}  ({p.Editor})").FontSize(10);
                            }
                        }
                    });

                    // ---------------- PIE DE PÁGINA ----------------
                    pagina.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("ECLIPSE  ·  Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(rutaArchivo);
        }

        static void SeccionTitulo(IContainer contenedor, string texto)
        {
            contenedor.PaddingBottom(4).Text(texto).FontSize(13).Bold();
        }

        static string ColorParaEstado(string estado) => estado switch
        {
            "Saludable" => Colors.Green.Darken1,
            "Regular" => Colors.Orange.Darken1,
            _ => Colors.Red.Darken1,
        };
    }
}