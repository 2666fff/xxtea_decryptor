# XXTEA Decryptor

A standalone Windows Forms utility for XXTEA file encryption and decryption.

This repository was extracted from the `XXTEADecrpty` project in `XXTEA_Decoder` so the generic XXTEA decoder can be maintained and shared independently.

## Features

- Encrypt or decrypt a single file.
- Process every file under an input directory.
- Configure the XXTEA sign/header and key in the UI.
- Keep the original directory layout when writing decoded output.

## Build

Open `XXTEADecrypt.sln` with Visual Studio and build the `XXTEADecrypt` project.

The project targets .NET Framework 4.5.2 and uses Windows Forms.

## Notes

- Build output, Visual Studio user settings, signing keys, and the local Codex task ledger are ignored by git.
- No license has been added in this extraction. Add one before relying on explicit redistribution terms.
