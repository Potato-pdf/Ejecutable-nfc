/**
 * NFC Reader para NFC Access Manager
 * 
 * Hardware:
 *   - Arduino Uno / Nano / Mega
 *   - Módulo MFRC522 (NFC/RFID SPI)
 * 
 * Librería necesaria:
 *   "MFRC522" by GithubCommunity — instala desde Arduino IDE:
 *   Herramientas → Administrar Bibliotecas → busca "MFRC522"
 * 
 * Conexión de pines (SPI):
 *   MFRC522  →  Arduino Uno/Nano
 *   ─────────────────────────────
 *   SDA      →  Pin 10 (SS/CS)
 *   SCK      →  Pin 13
 *   MOSI     →  Pin 11
 *   MISO     →  Pin 12
 *   IRQ      →  No conectar
 *   GND      →  GND
 *   RST      →  Pin 9
 *   3.3V     →  3.3V  ← IMPORTANTE: usar 3.3V, NO 5V
 * 
 * Protocolo serial hacia la app Windows:
 *   - Baud rate: 9600
 *   - Cuando se detecta una tarjeta, envía: "UID:XX:XX:XX:XX\n"
 *   - El UID se envía en hexadecimal mayúsculas separado por ":"
 */

#include <SPI.h>
#include <MFRC522.h>

#define SS_PIN  10
#define RST_PIN  9

MFRC522 mfrc522(SS_PIN, RST_PIN);

// Tiempo mínimo entre lecturas del mismo tag (ms)
#define DEBOUNCE_MS 2000

unsigned long lastReadTime = 0;
String lastUid = "";

void setup() {
  Serial.begin(9600);
  SPI.begin();
  mfrc522.PCD_Init();

  Serial.println("READY:NFC Reader listo. Acerca una tarjeta...");
}

void loop() {
  // Esperar nueva tarjeta
  if (!mfrc522.PICC_IsNewCardPresent()) return;
  if (!mfrc522.PICC_ReadCardSerial())   return;

  // Construir el UID como string hexadecimal con ":"
  String uid = "";
  for (byte i = 0; i < mfrc522.uid.size; i++) {
    if (i > 0) uid += ":";
    if (mfrc522.uid.uidByte[i] < 0x10) uid += "0";
    uid += String(mfrc522.uid.uidByte[i], HEX);
  }
  uid.toUpperCase();

  // Debounce: ignorar si es el mismo UID dentro del periodo
  unsigned long now = millis();
  bool sameUid    = (uid == lastUid);
  bool tooSoon    = (now - lastReadTime) < DEBOUNCE_MS;

  if (sameUid && tooSoon) {
    mfrc522.PICC_HaltA();
    mfrc522.PCD_StopCrypto1();
    return;
  }

  // Enviar UID al PC
  Serial.println("UID:" + uid);

  lastUid      = uid;
  lastReadTime = now;

  // Detener comunicación con la tarjeta actual
  mfrc522.PICC_HaltA();
  mfrc522.PCD_StopCrypto1();
}
