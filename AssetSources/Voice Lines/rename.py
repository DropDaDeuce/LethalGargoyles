import os

def rename_wav_files(directory):
    """Renames all .wav files in the specified directory by adding 'taunt_' to the beginning of their filenames."""

    for filename in os.listdir(directory):
        if (filename.endswith(".wav") or filename.endswith(".aup3")) and "taunt" not in filename:
            old_filepath = os.path.join(directory, filename)
            new_filename = f"taunt_general_{filename}"
            new_filepath = os.path.join(directory, new_filename)
            os.rename(old_filepath, new_filepath)

if __name__ == "__main__":
    directory = "D:\Projects\Lethal Company\LethalGargoyles\AssetSources\Voice Lines"
    rename_wav_files(directory)