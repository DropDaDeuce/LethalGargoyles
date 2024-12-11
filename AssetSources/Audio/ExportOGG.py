import os
import sys
import time
from pipeclient import PipeClient
from pywinauto import Application

def export_to_ogg():
    """
    Exports all Audacity projects in the same folder as the script to OGG format.
    Restarts Audacity between each file to avoid conflicts.
    Includes improved pipe handling and delays to prevent crashes.
    """

    script_folder = os.path.dirname(os.path.abspath(__file__))
    audacity_path = os.path.join("C:\\", "Program Files", "Audacity", "Audacity.exe")
    
    for filename in os.listdir(script_folder):
        if filename.endswith(".aup3"):
            project_path = os.path.join(script_folder, filename)
            ogg_path = os.path.splitext(project_path)[0] + ".ogg"
            basename = os.path.splitext(filename)[0]
            
            if os.path.exists(project_path):
                try:
                    # Start a new Audacity process for each file
                    client = PipeClient()
                    
                    # Open the project
                    client.write(f'OpenProject2: Filename="{project_path}"')
                    # Export to OGG (including NumChannels and Format parameters)
                    time.sleep(2)  # Wait for Audacity to start
                    app = Application(backend="uia").connect(path=audacity_path, title=basename)  # Use the audacity_path variable
                    main_window = app.window(title_re=basename) 
                    
                    client.write("SelectAll")
                    client.write(f'Export2: Filename="{ogg_path}" NumChannels=1.0')
                    
                    # Wait for the export to complete (poll for the OGG file)
                    max_attempts = 10
                    attempts = 0
                    while attempts < max_attempts:
                        if os.path.exists(ogg_path):
                            print(f"OGG file '{ogg_path}' created successfully.")
                            break
                        time.sleep(1)
                        attempts += 1

                    if attempts == max_attempts:
                        print(f"Timeout waiting for OGG file '{ogg_path}'. Skipping.")
                        continue  # Skip to the next file if the export times out
                    
                    
                    
                    main_window.close()
                    # Close the project (and Audacity)
                    client.close()
                    # Explicitly close the pipe connection
                    time.sleep(1)

                except Exception as e:
                    print(f"Error processing {filename}: {e}")
                    raise  # Or handle the exception as needed


if __name__ == "__main__":
    try:
        export_to_ogg()
    except Exception as e:
        print(f"An unexpected error occurred: {e}")
        raise
        