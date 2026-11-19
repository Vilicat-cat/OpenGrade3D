#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Decodificador de la telemetria del firmware OG3D (Parche 8, PGN 0x6A/0xE2 = 27362).

El monitor UDP de AgIO solo muestra el PGN y la IP de origen; este script decodifica
el payload completo segun la tabla de CAMBIOS_PARCHES.md (Parche 8).

Uso (requiere Python 3, sin dependencias; en Windows: "py" en vez de "python"):

  1) Pegando el hex de una trama (copiado de Packet Sender o Wireshark):
       python decodificar_telemetria.py 80816AE22200640B042C00...
       python decodificar_telemetria.py 80 81 6a e2 22 00 64 0b ...

  2) Escuchando la red en vivo (ejecutalo en un PC de la misma subred; la trama va
     a broadcast x.x.x.255 asi que llega a todos). En el PC donde corre AgIO el
     puerto 9999 ya esta ocupado: usa otro equipo, o cambia TELEM_PORT a 9998 en el
     firmware y escucha ese puerto:
       python decodificar_telemetria.py --listen
       python decodificar_telemetria.py --listen 9998

Nota: en vivo descarta silenciosamente el resto de tramas (hello, 0xE1, GNSS...)
y solo decodifica la telemetria 0x6A/0xE2.
"""
import socket
import struct
import sys

MAGIC = bytes([0x80, 0x81, 0x6A, 0xE2])  # 0x80 0x81 | src 0x6A | PGN 0xE2
LEN = 34                                  # payload
FRAME_SIZE = 5 + LEN + 1                  # 40 bytes

FIX_QUALITY = {0: 'sin fix', 1: 'GPS', 2: 'DGPS', 3: 'PPS', 4: 'RTK Fix',
               5: 'RTK Float', 6: 'estimado', 7: 'manual', 8: 'simulacion'}
FLAGS = [(0, 'workSwitch=manual/seguro'), (1, 'autoEnable'),
         (2, 'ordenando SUBIR'), (3, 'ordenando BAJAR'),
         (4, 'GNSS fresco'), (5, 'ajustes 0xB8 recibidos')]


def decodificar(frame, src='(hex pegado)'):
    """Decodifica una trama completa de 40 bytes. Devuelve True si era telemetria valida."""
    if len(frame) < FRAME_SIZE:
        print(f'  trama demasiado corta ({len(frame)} bytes; se esperaban {FRAME_SIZE})')
        return False
    f = bytes(frame[:FRAME_SIZE])
    if f[:4] != MAGIC:
        print('  cabecera no es 0x80 0x81 0x6A 0xE2')
        return False
    if f[4] != LEN:
        print(f'  longitud de payload inesperada: {f[4]} (se esperaba {LEN})')
        return False
    crc = sum(f[2:5 + LEN]) & 0xFF
    if crc != f[5 + LEN]:
        print(f'  CRC INCORRECTO: calculado 0x{crc:02X}, recibido 0x{f[5 + LEN]:02X}')
        return False

    fixq, sats, fixt, flags = f[5], f[6], f[7], f[8]
    hdop, speed, gga_age, watchdog = struct.unpack_from('<HHHH', f, 9)
    alt, target = struct.unpack_from('<ii', f, 17)
    cut, pwm = struct.unpack_from('<Hh', f, 25)
    baud, uptime = struct.unpack_from('<II', f, 29)
    version, reservado = f[37], f[38]

    fl = ', '.join(n for b, n in FLAGS if flags & (1 << b)) or 'ninguno'
    fresco = 'FRESCA' if flags & (1 << 4) else 'OBSOLETA -> FAILSAFE NEUTRO (Parche 1)'
    alt_s = 'SIN ALTURA (centinela)' if alt >= 22000000 else f'{alt / 1000:.3f} m'
    tgt_s = 'SIN OBJETIVO (manual)' if target >= 20000000 else f'{target / 1000:.3f} m'
    spd_s = 'invalida' if speed >= 32000 else f'{speed / 100:.2f} km/h'
    h = uptime // 3600
    mnt = (uptime % 3600) // 60

    print(f'PGN 0x6A/0xE2 (27362)  IP {src}  CRC OK  (formato v{version})')
    print(f'  GNSS : calidad {fixq} ({FIX_QUALITY.get(fixq, "?")}), {sats} sats, tipo {fixt}, HDOP {hdop / 100:.2f}')
    print(f'  GGA  : edad {gga_age} ms ({fresco}) | watchdog {watchdog} | GPS {baud} baudios')
    print(f'  ALT  : {alt_s} | objetivo: {tgt_s} | velocidad {spd_s}')
    print(f'  CUCHA: cutValve {cut} (100=neutro) | PWM {pwm} (+subir/-bajar)')
    print(f'  FLAGS: 0b{flags:08b} -> {fl} | uptime {uptime} s ({h}h{mnt:02d}m)')
    print(f'  HEX  : {f.hex(" ")}')
    return True


def extraer_telemetria(datagrama):
    """Busca 0x80 0x81 0x6A 0xE2 dentro de un datagrama (por si va concatenado)."""
    i = datagrama.find(MAGIC)
    if i < 0:
        return None
    return datagrama[i:i + FRAME_SIZE]


def modo_hex(args):
    hexs = ''.join(a for a in args if a not in ('-', '--')).replace(' ', '').replace(':', ',').replace(',', '')
    try:
        raw = bytes.fromhex(hexs)
    except ValueError:
        print('Hex no valido. Ejemplo: py decodificar_telemetria.py 80816AE222...')
        return 1
    print(f'Trama pegada: {len(raw)} bytes')
    return 0 if decodificar(raw) else 1


def modo_listen(puerto):
    sk = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sk.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        sk.bind(('', puerto))
    except OSError as e:
        print(f'No se pudo abrir el puerto UDP {puerto}: {e}')
        print('Si AgIO corre en este PC, usa otro equipo o cambia TELEM_PORT en el firmware (p.ej. 9998).')
        return 1
    print(f'Escuchando telemetria 0x6A/0xE2 en UDP {puerto}... (Ctrl+C para salir)')
    print('(las tramas de otros PGNs se ignoran)\n')
    try:
        while True:
            data, addr = sk.recvfrom(2048)
            frame = extraer_telemetria(data)
            if frame:
                decodificar(frame, src=addr[0])
    except KeyboardInterrupt:
        print('\nSaliendo.')
    finally:
        sk.close()
    return 0


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 1
    if args[0] == '--listen':
        return modo_listen(int(args[1]) if len(args) > 1 else 9999)
    return modo_hex(args)


if __name__ == '__main__':
    sys.exit(main())
