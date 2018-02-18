﻿using System;
using System.Drawing;

namespace LogOut {
    public class HealthBarTracker {
        private Bitmap img;
        private Graphics gfx;
        private Size size;
        private int yCoord;
        private int[] currentHealthState;

        private static int areaMulti;
        private static int offset;

        /// <summary>
        /// Changes image capture position and size
        /// </summary>
        public void SetLocation() {
            areaMulti = 1 + Settings.captureAreaMultiplier * 2;

            size = new Size(Settings.width, Settings.height * areaMulti);
            currentHealthState = new int[Settings.width];
            yCoord = Settings.height / 4 ;

            img = new Bitmap(Settings.width, Settings.height * areaMulti, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            gfx = Graphics.FromImage(img);

            MainWindow.healthBar.SetPercentage(100);
        }

        /// <summary>
        /// Main loop of sorts
        /// </summary>
        public void PollHealth_Task() {
            bool lastNotBelowLimit = true;
            double health, lastHealth = 0;

            // Wait until program has found PoE process
            while (Settings.top < 1 || gfx == null) System.Threading.Thread.Sleep(10);
            
            while (true) {
                try {
                    // Run x times a second
                    System.Threading.Thread.Sleep(Settings.healthPollRateMS);

                    // Don't do any calculations until health tracking has been enabled
                    if (!Settings.trackHealth) continue;

                    // Take screenshot of health bar
                    gfx.CopyFromScreen(Settings.left, Settings.top - Settings.height, 0, 0, size, CopyPixelOperation.SourceCopy);

                    ParseHealth();
                    health = GetEHPAsPercentage();

                    // Do nothing if state has not changed
                    if (health == lastHealth) continue;
                    else lastHealth = health;

                    if (Math.Round(health) < 1) {
                        MainWindow.Log(" HealthBar not visible", -1);
                        continue;
                    }

                    // Update healthbar overlay
                    if (Settings.healthBarEnabled) {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => MainWindow.healthBar.SetPercentage(health));
                    }

                    // If topmost window is not PoE
                    if (!Win32.IsTopmost()) continue;

                    // Debugging, I guess?
                    if (health > Settings.healthLimitPercent)
                        MainWindow.Log("[Health] Found change: " + health, -1);

                    // Do action when health is below limit
                    if (health < Settings.healthLimitPercent) {
                        if (lastNotBelowLimit) {
                            // Raise flag so this is not spammed
                            lastNotBelowLimit = false;
                            MainWindow.Log("[Health] Health below limit (" + health + ")", 0);

                            // Quit game if event is enabled in settings
                            if (Settings.doLogout) {
                                MainWindow.Log("[Health] Sending disconnect signal", 0);
                                long delay = KillTCP.KillTCPConnectionForProcess();
                                MainWindow.Log("[Health] Disconnected (took " + delay + "ms)", 0);
                            }
                        }
                    } else {
                        lastNotBelowLimit = true;
                    }
                } catch (Exception ex) {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// Extract pixels from captured image
        /// </summary>
        public void ParseHealth() {
            offset = FindBarOffset();
            if (offset < 1) return;

            // Update healthbar overlay tracker
            if (Settings.showCaptureOverlay) System.Windows.Application.Current.Dispatcher.Invoke(() => {
                MainWindow.healthOverlay.Width = Settings.width;
                MainWindow.healthOverlay.Height = Settings.height;
                MainWindow.healthOverlay.Left = Settings.left;
                MainWindow.healthOverlay.Top = Settings.top + offset - Settings.height * 2;
            });

            // For refrence
            //for (int i = 0; i < 10; i++) img.SetPixel(5 + i, offset, Color.FromArgb(255, 0, 0));
            //img.Save("Screenshot.png", System.Drawing.Imaging.ImageFormat.Png);

            // Fill pixel array
            for (int x = 0; x < Settings.width; x++) {
                Color color = img.GetPixel(x, offset - Settings.height + yCoord);
                currentHealthState[x] = FindColorMatch(color);

                //if (match == -1) {
                //    Console.WriteLine("    Bad color (offset {0}): r:{1} g:{2} b:{3}", offset, color.R, color.G, color.B);
                //    img.SetPixel(x, offset - Settings.height + yCoord + 1, Color.FromArgb(0, 255, 0));
                //    img.SetPixel(x, offset - Settings.height + yCoord - 1, Color.FromArgb(0, 255, 0));
                //    img.Save("Screenshot_3.png", System.Drawing.Imaging.ImageFormat.Png);
                //}

                //if (match == -1) Console.Write(" ");
                //else Console.Write(match);
            }
            //Console.WriteLine();
        }

        /// <summary>
        /// Finds offset of healthbar
        /// </summary>
        /// <returns>How many px away is bottom border</returns>
        private int FindBarOffset() {
            for (int y = Settings.height * areaMulti - 1; y > 0; y--) {
                Color yColor = img.GetPixel(Settings.barCaptureOffset, y);

                //img.SetPixel(localOffset, y, Color.FromArgb(0, 0, 255));

                int yMatch = FindBorderColorMatch(yColor);
                if (yMatch == -1) continue;

                //Console.Write("offset " + y + ": ");

                int count = 0;
                for (int x = Settings.barCaptureOffset + 1; x < Settings.width - Settings.barCaptureOffset * 2; x++) {
                    Color xColor = img.GetPixel(x, y);
                    //img.SetPixel(x, y, Color.FromArgb(255, 0, 0));

                    int xMatch = FindBorderColorMatch(xColor);
                    //Console.Write(xMatch);
                    if (xMatch == -1) {
                        //Console.WriteLine("Invalid color. Offset {0}: r:{1} g:{2} b:{3}", offset, xColor.R, xColor.G, xColor.B);
                        //img.Save("Screenshot_1.png", System.Drawing.Imaging.ImageFormat.Png);
                        count = 0;
                        break;
                    }
                    count++;
                }

                //Console.WriteLine();
                if (count > Settings.width - Settings.barCaptureOffset * 4) return y;
            }

            //Console.WriteLine("Invalid offset");
            //img.Save("Screenshot_2.png", System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }

        /// <summary>
        /// Matches extracted pixels to preset colors
        /// </summary>
        /// <param name="color">Color to match</param>
        /// <returns>(See settings for descriptions)</returns>
        public int FindColorMatch(Color color) {
            for (int x = 0; x < Settings.topBar.GetLength(0); x++) {
                if (color.R > Settings.topBar[x, 0, 0] && color.R < Settings.topBar[x, 0, 1]) {
                    if (color.G > Settings.topBar[x, 1, 0] && color.G < Settings.topBar[x, 1, 1]) {
                        if (color.B > Settings.topBar[x, 2, 0] && color.B < Settings.topBar[x, 2, 1]) {
                            return x;
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Matches extracted pixels to preset colors
        /// </summary>
        /// <param name="color">Color to match</param>
        /// <returns>(See settings for descriptions)</returns>
        public int FindBorderColorMatch(Color color) {
            for (int x = 0; x < Settings.bottomBar.GetLength(0); x++) {
                if (color.R > Settings.bottomBar[x, 0, 0] && color.R < Settings.bottomBar[x, 0, 1]) {
                    if (color.G > Settings.bottomBar[x, 1, 0] && color.G < Settings.bottomBar[x, 1, 1]) {
                        if (color.B > Settings.bottomBar[x, 2, 0] && color.B < Settings.bottomBar[x, 2, 1]) {
                            return x;
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Gets percentage from extracted pixels
        /// </summary>
        /// <returns>Remaining health as 0-100</returns>
        public double GetEHPAsPercentage() {
            double proL = 0, conL = 0;
            double proE = 0, conE = 0;

            // Get Life and ES
            for (int i = 0; i < Settings.width; i++) {
                switch (currentHealthState[i]) {
                    case 0:
                    case 1:
                        proL++;
                        proE++;
                        break;
                    case 2:
                    case 3:
                    case 4:
                        proE++;
                        conL++;
                        break;
                    case 5:
                    case 6:
                        proL++;
                        conE++;
                        break;
                    case 7:
                        conL++;
                        conE++;
                        break;
                }
            }

            // Get percentages of both pools
            double prL = proL / (proL + conL) * 100.0;
            double prE = proE / (proE + conE) * 100.0;

            // Get weights of both pools
            double eHP = Settings.total_life + Settings.total_es;
            double weL = Settings.total_life / eHP;
            double weE = Settings.total_es / eHP;

            return Math.Round(prL * weL + prE * weE, 3);
        }
    }
}
