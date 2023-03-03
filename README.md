# CO2Mon [WIP]

A desktop application for monitoring of CO2 levels with a MH-Z19(B) sensor.

These sensors already incorporate an STM32 microcontroller and support UART output, so I see no point in additional glue logic like AVR-Arduino
(unless you want multiple sensors, that is). Therefore I decided to write a simple desktop program to collect data from the sensor, that requires only a USB-UART converter.

Features:
 - Reading "non-normalized" and "normalized" CO2 levels
 - Live plot [WIP]
 - CSV log [WIP]
 - Cross-platform (.NET 6.0 + Avalonia UI)
 - Some configuration tools will be added later, probably [WIP]
