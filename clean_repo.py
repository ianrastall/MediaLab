import os
import stat
import shutil
from pathlib import Path

def remove_readonly(func, path, _):
    """Clears the read-only flag and retries the deletion."""
    try:
        os.chmod(path, stat.S_IWRITE)
        func(path)
    except Exception as e:
        print(f"Could not remove {path}: {e}")

def clean_artifacts():
    # Get the directory where the script is located (repo root)
    root_dir = Path(__file__).parent.resolve()

    # Folders and file extensions to purge
    target_dirs = {
        'bin', 'obj', '.vs', 'TestResults', 'packages', 
        '__pycache__', '.pytest_cache', 'build', 'dist'
    }
    target_exts = {'.user', '.suo', '.log', '.tmp'}

    print(f"Starting cleanup in: {root_dir}")
    print("-" * 40)
    
    deleted_dirs = 0
    deleted_files = 0

    # Walk bottom-up to safely delete directories without breaking the iteration
    for dirpath, dirnames, filenames in os.walk(root_dir, topdown=False):
        current_path = Path(dirpath)

        # 1. Delete matching files
        for file in filenames:
            file_path = current_path / file
            if file_path.suffix in target_exts:
                try:
                    file_path.unlink()
                    print(f"Deleted file: {file_path.relative_to(root_dir)}")
                    deleted_files += 1
                except PermissionError:
                    # Handle read-only files
                    os.chmod(file_path, stat.S_IWRITE)
                    file_path.unlink()
                    print(f"Deleted read-only file: {file_path.relative_to(root_dir)}")
                    deleted_files += 1
                except Exception as e:
                    print(f"Failed to delete {file_path.name}: {e}")

        # 2. Delete matching directories
        for d in dirnames:
            if d in target_dirs:
                dir_path = current_path / d
                try:
                    # onerror handler deals with read-only files inside the directories
                    shutil.rmtree(dir_path, onerror=remove_readonly)
                    print(f"Deleted directory: {dir_path.relative_to(root_dir)}")
                    deleted_dirs += 1
                except Exception as e:
                    print(f"Failed to delete {d}: {e}")

    print("-" * 40)
    print(f"Cleanup complete! Removed {deleted_dirs} directories and {deleted_files} files.")

if __name__ == "__main__":
    clean_artifacts()