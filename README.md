# PetTemplate

Full-stack pet project template: ASP.NET Core API + Angular frontend.

## Stack

- **Backend:** ASP.NET Core (.NET 10), MySQL
- **Frontend:** Angular
- **Infrastructure:** Docker Compose

## Usage

### Install template from GitHub

```bash
git clone https://github.com/pobochniy/pet-template ~/proj/pet-template && dotnet new install ~/proj/pet-template
```

### Create new project

```bash
dotnet new pet -n MyAwesomeProject -o folterName
```

This will create a new folder `folterName/` with all `PetTemplate` references replaced by `MyAwesomeProject`.

### Update template

After pushing changes to the repository, re-clone and reinstall with `--force` to overwrite the local copy:

```bash
rm -rf ~/proj/pet-template && git clone https://github.com/pobochniy/pet-template ~/proj/pet-template && dotnet new install ~/proj/pet-template --force
```

### Uninstall template

```bash
dotnet new uninstall ~/proj/pet-template
```

## Local development

### Install template locally

```bash
dotnet new install ./
```

### Run infrastructure

```bash
docker-compose up -d
```
